# IoP Profile Server - Architecture


## Profile Server in IoP Network

### Connections to Profile Servers Network

Profile servers form a network of servers that hold information about identity profiles of end users and client applications and allow these identities to find and interact with each other. 
The profile server network is formed of neighborhoods. Vaguely, a neighborhood is a group of profile servers that are geographically close to each other. Within the neighborhood, profile servers
share profiles of the identities hosted among them. This allows the servers within a neighborhood to answer search queries about all identity profiles within the neighborhood. A neighborhood 
is a subjective view of each profile server, which means that if a profile server considers two other servers as its neighbors, those two servers might not consider to be neighbor of each other.


### Connections to Location Based Network

Profile servers do not manage their neighborhoods relationship by themselves. Each profile server relies on a [Location Based Network server](https://github.com/Fermat-ORG/iop-location-based-network) (LOC server)
that runs locally with it to provide information about the profile server's neighborhood. The profile server thus only has some information about its own neighborhood, but it has no information about 
the other parts of the network.

A profile server communicates with its LOC server over a trusted local TCP channel using [IoP Location Based Network protocol](https://github.com/Internet-of-People/message-protocol/blob/master/IopLocNet.proto).

Note that while each profile server needs its associated LOC server to run on the same machine, not every LOC server needs to be associated with a profile server.


### Connections to Content Address Network

Content Address Network (CAN) is a network of servers that can store arbitrary content and allows it to be found and downloaded. Profile servers use CAN servers for two different purposes. 
In the first place, profile servers use CAN as an indexing service that allows members of IoP network to find contact information to profile servers using their network identifiers.
Secondly, profile servers allow their clients to indirectly store content to CAN network. In this case, the profile server plays a role of an authorization layer which is missing in CAN.

Each profile server expects a CAN server to run with it on the same machine. A profile server communicates with its CAN server over a trusted local TCP channel using API that CAN server provies.

Note that while each profile server needs its associated CAN server to run on the same machine, not every CAN server needs to be associated with a profile server.


### Connections to Client Applications

Client applications and their users are represented by identities in the IoP network. To the identities, profile servers offer the following main services:

 * profile hosting,
 * providing information about profiles of hosted identities,
 * online communication with hosted identities,
 * searching for profiles within the neighborhood,
 * storing user data to CAN.

Some of these services are offered only to the clients hosted on the particular profile server, other services are available to all clients.


### Connections to Other Networks

Profile server does not connect directly to any other network except for those mentioned above. Clients may need to use services offered by profile servers to be able to work in with other networks 
and servers in IoP network, but profile servers do not need to understand the details of their protocols. For example, the WebRTC protocol for direct client to client communication requires 
signalling, which is where profile servers' online communication between identities can be used, but profile servers will not analyze nor understand the messages transferred in those channels. 


---
*IoP Network, Profile Servers point of view*

![IoP Network, Profile Servers point of view](images/iop-network.png "IoP Network, Profile Servers point of view")

