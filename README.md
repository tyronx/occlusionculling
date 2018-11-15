**Description**

This is a ray casting chunk culling system inspired by Tommos cave culling algorithm for minecraft (https://tomcc.github.io/index.html)
It is essentially an ray-casting implementation of Tommos "Part 2" which avoids the underculling issues when using a Breadth-First search.
Several tricks are being employed to reduce the cost of ray casting:

- Instead of ray casting every loaded chunk, we only ray cast towards chunks at the very edge of the loaded area. This works because doing so we will visit all other chunks anyway
- Skip every chunk outside the view frustum. This can be done simply by taking the dot product of the players view vector and the chunk direction vector (which is the chunk position minus the player position)
- Using large cubic chunks of size 32x32x32 reduces the amount of steps we have to take. This can be independent of actual chunk size, as you can just define arbitrary chunk sizes during the traversability test (see "Part 1" in tommos blog)
- Empirical evidence shows that 2 traces per chunk works in almost every case


**Performance results**

Setup: i5-6300GQ CPU @ 2.30 Ghz, at 192 blocks view distance and at a field of view of 75 degrees.

Results: 1ms or 1062 traces per frame
 
<img src="https://raw.githubusercontent.com/tyronx/occlusionculling/master/cullingOff.png" alt="culling off" width="400" align="left"/>
<img src="https://raw.githubusercontent.com/tyronx/occlusionculling/master/cullingOn.png" alt="culling on" width="400" align="left"/>
