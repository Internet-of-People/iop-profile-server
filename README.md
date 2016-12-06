![alt text](https://raw.githubusercontent.com/Fermat-ORG/media-kit/00135845a9d1fbe3696c98454834efbd7b4329fb/MediaKit/Logotype/fermat_logo_3D/Fermat_logo_v2_readme_1024x466.png "Fermat Logo")

# IoP Profile Server

## TODOs and Possible Improvements

### DoS and Sybil Attack using Neighborhood Initialization Process

Currently, there is no verification whether an incoming profile server that requests uploading its profile database is authorized to do so. 
Mitigation of this problem depends on design decisions to be made about the final definition of the server neighborhood.

Depending on the neighborhood design and definitions is also the possibility of spawning a large number of servers within a certain location.
Mitigation of this should probably be done on LBN and CAN level with IP subnet based limitation.

Also currently, there is no limit on number of attempts for the Neighborhood Initialization Process if it fails. This allows the attacker to 
perform a DoS. To mitiage this issue, we can introduce IP based limit.


### DoS Attack Using Search Queries, Profile Updates, and Other Requests

Currently, there is no limit on a number of search queries that a single identity can send to the profile server. Sending a search query is a cheap 
operation for the client compared to the amount of work that the server is potentially doing.

Similarly, there are currently no limits on other requests such as profile updates.

To mitigate this issue, we would need to introduce identity based or IP based limits on search queries and other requests.



### Sybil Attack on Profile Hosting Registration

Currently, there is no limit on a number of profile that a single IP address can register on the server. A single attacker can occupy all free slots 
the profile server has for hosting identities. 

To mitigate this issue, we would need to introduce IP based limits on search queries.




### Updates Between Neighbors

Neighbor servers share their profile databases and keep their information synchronized. The initial database upload to a neighbor is efficient, 
but individual updates that follows are somehow inefficient as we currently use a new TCP TLS connection to the target neighbor, verify our identity 
and send a single update of a single profile. 

As the number of neighbors is potentially high and the frequency of changes in the hosted profiles is low, reusing a connection does not seem 
to be a good option unless it is used by both peers. Such optimizations should not be done until the final design of the server neighborhood 
is decided.

Making batch updates instead of individual updates would save resources but it would potentially affect the UX as the profile search feature would 
greatly suffer. 



### Unused Images in Images Folder

Currently, there is no garbage collector that would remove unused images from the images folder. If the server process is terminated in certain 
situations, image files that are not linked to any records in the database can be created. 

To solve this problem, a garbage collector could be implemented, that could possibly run during the profile server startup and that would 
delete all images in the images folder that are not referenced from the database. 