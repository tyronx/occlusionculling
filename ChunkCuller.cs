using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Vintagestory.Client
{
    public class ChunkCuller
    {
        ClientMain game;
        int chunksize;

        public Ray ray = new Ray();
        
        Vec3d planePosition = new Vec3d();

        Vec3i[] cubicShellPositions;
        Vec3f[] cubicShellPositionsNormalized;

        Vec3i centerpos = new Vec3i();
        bool isAboveHeightLimit;

        Vec3f playerViewVec;

        public Vec3i curpos = new Vec3i();
        Vec3i toPos = new Vec3i();


        public ChunkCuller(ClientMain game)
        {
            this.game = game;
            chunksize = game.WorldMap.ChunkSize;

            ClientSettings.Inst.AddWatcher<int>("viewDistance", GenShellVectors);
            GenShellVectors(ClientSettings.ViewDistance);

            ClientSettings.Inst.AddWatcher<bool>("occlusionculling", occlusioncullingModeChanged);
        }
        
        void occlusioncullingModeChanged(bool on)
        {
            if (on) return;

            lock (game.WorldMap.chunksLock)
            {
                foreach (var val in game.WorldMap.chunks) val.Value.SetVisible(true);
            }
        }


        // Preloads all shell positions and normalized variants of those positions so we dont't have to retrieve them every frame
        void GenShellVectors(int viewDistance)
        {
            // Vintage Story loaded chunk radius forms an octagonal shape
            Vec2i[] points = ShapeUtil.GetOctagonPoints(0, 0, viewDistance / chunksize + 1);
            int cmapheight = game.WorldMap.ChunkMapSizeY;

            HashSet<Vec3i> shellPositions = new HashSet<Vec3i>();

            for (int i = 0; i < points.Length; i++)
            {
                Vec2i point = points[i];
                for (int cy = -cmapheight; cy <= cmapheight; cy++)
                {
                    shellPositions.Add(new Vec3i(point.X, cy, point.Y));
                }
            }

            for (int r = 0; r < viewDistance/chunksize + 1; r++)
            {
                points = ShapeUtil.GetOctagonPoints(0, 0, r);
                for (int i = 0; i < points.Length; i++)
                {
                    Vec2i point = points[i];
                    // We do overextend the shell positions on the vertical axis as we seem to have overculling issues otherwise
                    shellPositions.Add(new Vec3i(point.X, -cmapheight, point.Y));
                    shellPositions.Add(new Vec3i(point.X, cmapheight, point.Y));
                }
            }

            cubicShellPositions = shellPositions.ToArray();

            cubicShellPositionsNormalized = new Vec3f[cubicShellPositions.Length];
            for (int i = 0; i < cubicShellPositions.Length; i++)
            {
                cubicShellPositionsNormalized[i] = new Vec3f(cubicShellPositions[i]).Normalize();
            }
        }


        public void CullInvisibleChunks()
        {
            if (!ClientSettings.Occlusionculling || game.WorldMap.chunks.Count < 100)
            {
                return;
            }

            Vec3d camPos = game.player.Entity.CameraPos;
            centerpos.Set((int)(camPos.X / chunksize), (int)(camPos.Y / chunksize), (int)(camPos.Z / chunksize));

            isAboveHeightLimit = centerpos.Y >= game.WorldMap.ChunkMapSizeY;

            playerViewVec = EntityPos.GetViewVector(game.mousePitch, game.mouseYaw).Normalize();
            
            lock (game.WorldMap.chunksLock)
            {
                foreach (var val in game.WorldMap.chunks)
                {
                    val.Value.SetVisible(false);
                }

                // We sometimes have issues with chunks adjacent to the player getting culled, so lets make these always visible
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            long index3d = game.WorldMap.ChunkIndex3D(dx + centerpos.X, dy + centerpos.Y, dz + centerpos.Z);
                            ClientChunk chunk=null;

                            if (game.WorldMap.chunks.TryGetValue(index3d, out chunk))
                            {
                                chunk.SetVisible(true);
                            }
                        }
                    }
                }
            }


            // Add some 15 extra degrees to the field of view angle for safety
            float fov = GameMath.Cos(game.MainCamera.Fov + 15 * GameMath.DEG2RAD);

            for (int i = 0; i < cubicShellPositions.Length; i++)
            {
                Vec3i vec = cubicShellPositions[i];

                float dotProd = playerViewVec.Dot(cubicShellPositionsNormalized[i]);
                if (dotProd <= fov/2) 
                {
                    // Outside view frustum
                    continue;
                }

                // It seems that one trace can cause issues where chunks are culled when they shouldn't
                // 2 traces with a y-offset seems to mitigate most of that issue
                TraverseRayAndMarkVisible(centerpos, vec, 0.25);
                TraverseRayAndMarkVisible(centerpos, vec, 0.75);
            }
        }



        void TraverseRayAndMarkVisible(Vec3i fromPos, Vec3i toPosRel, double yoffset = 0.5)
        {
            ray.origin.Set(fromPos.X + 0.5, fromPos.Y + yoffset, fromPos.Z + 0.5);
            ray.dir.Set(toPosRel);

            toPos.Set(fromPos.X + toPosRel.X, fromPos.Y + toPosRel.Y, fromPos.Z + toPosRel.Z);
            curpos.Set(fromPos);

            BlockFacing fromFace = null, toFace;

            int manhattenLength = fromPos.ManhattenDistanceTo(toPos);
            int curMhDist;
            
            while ((curMhDist = curpos.ManhattenDistanceTo(fromPos)) <= manhattenLength + 2)
            {
                // Since chunks are arranged in a uniform grid, all we have to do is to find out on 
                // what facing (N/E/S/W/U/D) the ray leaves the chunk and move into that direction.
                // This may seem inaccurate, but works surpisingly well
                toFace = GetExitingFace(curpos);
                if (toFace == null) return;

                long index3d = ((long)curpos.Y * game.WorldMap.chunkMapSizeZFast + curpos.Z) * game.WorldMap.chunkMapSizeXFast + curpos.X;

                ClientChunk chunk = null;
                game.WorldMap.chunks.TryGetValue(index3d, out chunk);
                
                if (chunk != null) {
                    chunk.SetVisible(true);

                    if (curMhDist > 1 && !chunk.IsTraversable(fromFace, toFace))
                    {
                        break;
                    }
                }

                curpos.Offset(toFace);
                fromFace = toFace.GetOpposite();

                if (!game.WorldMap.IsValidChunkPosFast(curpos.X, curpos.Y, curpos.Z) && (!isAboveHeightLimit || curpos.Y <= 0))
                {
                    break;
                }
            }
        }


        // Ray-Plane intersection test
        // Based on http://www.scratchapixel.com/lessons/3d-basic-rendering/minimal-ray-tracer-rendering-simple-shapes/ray-plane-and-ray-disk-intersection
        Vec3d pt = new Vec3d();
        Vec3d pHit = new Vec3d();
        private BlockFacing GetExitingFace(Vec3i pos)
        {
            for (int i = 0; i < BlockFacing.ALLFACES.Length; i++)
            {
                BlockFacing blockSideFacing = BlockFacing.ALLFACES[i];
                Vec3i planeNormal = blockSideFacing.Normali;

                double demon = planeNormal.X * ray.dir.X + planeNormal.Y * ray.dir.Y + planeNormal.Z * ray.dir.Z;

                if (demon > 0.00001)
                {
                    planePosition.Set(pos).Add(blockSideFacing.PlaneCenter);

                    pt.Set(planePosition.X - ray.origin.X, planePosition.Y - ray.origin.Y, planePosition.Z - ray.origin.Z);
                    double t = (pt.X * planeNormal.X + pt.Y * planeNormal.Y + pt.Z * planeNormal.Z) / demon;

                    if (t >= 0)
                    {
                        pHit.Set(ray.origin.X + ray.dir.X * t, ray.origin.Y + ray.dir.Y * t, ray.origin.Z + ray.dir.Z * t);

                        if (Math.Abs(pHit.X - planePosition.X) <= 0.5 && Math.Abs(pHit.Y - planePosition.Y) <= 0.5 && Math.Abs(pHit.Z - planePosition.Z) <= 0.5)
                        {
                            return blockSideFacing;
                        }
                    }
                }
            }

            return null;
        }
    }
}
