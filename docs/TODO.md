# IoP Profile Server - TO-DOs and Possible Improvements



## Features Not Yet Implemented

Some of the following features are going to be implemented, others will be considered.

### Network Simulator LOC Support

The network simulator is a tool that allows developers to run multiple instances of the profile server on a single machine and create a testing network, 
in which various scenarios can be played. Currently, the network simulator implements a dummy LOC server, which simulates a basic functionality of a LOC server.
We need to improve the network simulator to support real LOC software, to help us testing LOC functionality within the simulator as well as the integration 
between the profile server and the LOC server.


### Multimachine Network Simulator Support

Currently, the network simulator can only run on a single machine, which limits the size of the simulated network because of the simulator's demands on hardware resources.
It may be possible to extend the functionality of the network simulator to support execution on multiple machines, which would allow it to simulate large 
network environments on just a couple of testing servers.


### Profile Changes Notification

Some end user IoP applications may be interested in being notified every time a certain profile is updated on its profile server.
Currently, there is no system of offline messages, so it is expected that the notification could only be provided if the interested application 
has an open connection to the profile server that hosts the monitored profile. However, such an implementation could singnificantly increase
the resources consumed by the profile server, if the number of watchers is high.


### Hosting Plans and Invoicing

Currently, the profile server does not charge anything for its services and everyone is free to register and use it, unless the profile server hits 
its configured limits. A system of hosting plans is intended to limit the free use of the profile server by introducing various quotas on each 
functionality the profile server offers. It is expected that each profile server will offer a very limited free hosting plan that will allow 
new network users to join the network free of charge, as well as to offer paid plans for users that are able to provide monthly payments.

Invoicing is the intended system of payment requests delivered to the clients to ask them to pay for the profile server services, to its wallet.


### Backup Node

To prevent losing an access to the network when a client's hosting profile server is not available, a system of backup nodes can be created.
A backup node will contain up to date profile information about the client, but it will not be used until the client requests it due to problems 
with its primary hosting server. The backup node will then replace the role of the client's hosting server until its primary server is available again.
In case of permanent unavailability of the primary server, the client is expected to fully migrate to the backup server, or another profile server.



### Admin Interface

A special interface for the administrator of the profile server should be implemented to allow easier management and change of profile server settings 
without a need to restart it, as well as to provide various statistics about the profile server's operations and performances.


### Regression Test Mode

Once the admin interface is ready, we can implement a regression test mode that will allow developers to create new kinds of tests of the profile server.




### DoS Protection and Blacklisting

See [Security](#security) below.


## Security

### DoS and Sybil Attack using Neighborhood Initialization Process

Currently, there is no verification whether an incoming profile server that requests uploading its profile database is authorized to do so. 
Mitigation of this problem depends on design decisions to be made about the final definition of the server neighborhood.

Regardless of the neighborhood design and definitions, there is also the possibility of spawning a large number of servers within a certain location.
Mitigation of this should probably be done on LOC level with IP subnet based limitation.

Also currently, there is no limit on a number of attempts for the Neighborhood Initialization Process if it fails. This allows the attacker to 
perform a DoS attack. To mitigate this issue, we can introduce IP based limits.


### DoS Attack Using Search Queries, Profile Updates, and Other Requests

Currently, there is no limit on a number of search queries that a single identity can send to the profile server. Sending a search query is a cheap 
operation for the client compared to the amount of work that the server is potentially doing.

Similarly, there are currently no limits on other requests such as profile updates.

To mitigate this issue, we would need to introduce identity based or IP based limits on search queries and other requests.


### Sybil Attack on Profile Hosting Registration

Currently, there is no limit on a number of profiles that a single IP address can register on the server. A single attacker can occupy all free slots 
the profile server has for hosting identities. 

To mitigate this issue, we would need to introduce IP based limits on hosting registrations.



## Optimizations

### Updates Between Neighbors

Neighbor servers share their profile databases and keep their information synchronized. The initial database upload to a neighbor is efficient, 
but individual updates that follow are somehow inefficient as we currently use a new TCP TLS connection to the target neighbor, verify our identity 
and send a single update of a single profile even if there are more updates to be done. 

As the number of neighbors is potentially high and the frequency of changes in the hosted profiles is low, reusing a connection does not seem 
to be a good option unless it is used by both peers. Such optimizations should not be done until the final design of the server neighborhood 
is decided because, it is currently uncertain whether any optimization is needed.

Making batch updates instead of individual updates would save resources but it would potentially affect the UX as the profile search feature would 
greatly suffer. 



