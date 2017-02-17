# Profile Server Network Layer

On the network layer, the profile server communicates using three different network protocols to connect to other servers and clients in the IoP network.
This gives us a chance to divide this layer into three groups of components and modules by the protocol they are mostly connected to.


## Profile Server Protocol

### Network Server Component

This compoment creates TCP role servers based on the profile server configuration and manages their life cycle. 
Its second responsibility is to detect and terminate inactive connections to TCP role servers. Clients that want to be connected 
over a long period of time have to actively keep their connections alive.


### TCP Role Server

Profile server runs multiple roles (also called an interface of a profile server) where each serve different purposes and types of clients. Each TCP role server represents a single open TCP port
on which one or more roles can be served. Currently, the following interfaces are present:

 * Primary Interface is the primary contact point of the profile server. When we publish a profile server contact information anywhere in the IoP network 
it points to this interface. It is an unencrypted interface on which other peers can get information about where to find other profile server's interfaces.
 * Neighbors Interface is an encrypted interface for other profile servers that the profile server recognizes as its neighbors.
 * Non Customer Clients Interface is an encrypted interface for client applications that represent identities not hosted by this profile server.
 * Customer Clients Interface is an encrypted interface for client applications that represent identities hosted by this profile server.
 * Application Service Interface is an encrypted interface that is closely related to [Relay Connection](#relay-connection) and [Application Services](#application-services) modules. 
It is used when an online hosted identity is called through its application service by another identity that may or may not be hosted by the same profile server.
This interface then acts as a bridge between the two clients and the profile server only forwards the messages between them.

The profile server's administrator can configure each role to run on a separate TCP port, or to use a single port for multiple roles. 
Two roles can be served over a single TCP port if the communication on them is either encrypted on both of them or unencrypted on both of them. 
An encrypted interface cannot be combined with an unencrypted one on the same port.


### Incoming Client

Incoming client represents an incoming TCP connection that is accepted by one of the TCP role servers, and it holds the context information for the session. 
A connection can be made by an end user application, or by another profile server, or any other software that wants to communicate via Profile Server 
Network Protocol with the profile server.

A session can be anonymous or authenticated, in which case the connection represents a single cryptographic identity. The session context 
may contain any of the following information:

 * Information related to the conversation and authentication status of the client and its identity.
 * In case of hosted identities only, a list of application services that the client supports.
 * In case of a connection to Application Service Interface, a reference to the relay object (see [Relay Connection](#relay-connection) below). 
 * Search result cache (see ProfileSearchRequest call in [Profile Server Network Protocol](https://github.com/Internet-of-People/message-protocol/blob/master/IopProfileServer.proto) for more information).
 * In case of a neighbor connection, information about the status of the neighborhood initialization process (see StartNeighborhoodInitializationRequest call in [Profile Server Network Protocol](https://github.com/Internet-of-People/message-protocol/blob/master/IopProfileServer.proto) for more information).
 * List of requests sent to the client for which no response has been received and processed yet.



### Message Processor

Message processor is the module that is responsible for validation and processing of all Profile Server Network protocol messages that are received by incoming clients.
Most of the messages are fully processed here except for some more complicated cases, such as application service messages relayed over the Application Service Interface.


### Relay Connection

Represents and implements the processing logic of a bridge between a hosted identity and another identity that invokes an application service call to it. 
For more information about the mechanics of application service calls, see [Profile Server Network Protocol](https://github.com/Internet-of-People/message-protocol/blob/master/IopProfileServer.proto).


### Application Services

Represents a list of names of application services that an online hosted client supports.



## Location Based Network Protocol

### Location Based Network Component

When the profile server starts, this component tries to connect to a LOC server running on the same machine. If it succeeds, the profile server tells 
the LOC server on which port its primary interface can be found and asks the LOC server for information about the current view of the profile server's neighborhood. 
The connection to the LOC server is kept open so that the LOC server is able to send any updates in the neighborhood structure to the profile server.
When a change happens, this component creates a new neighborhood action which is then processed by [Neighborhood Action Processor](#neighborhood-action-processor).

Despite the periodical updates sent by the LOC server to the profile server, it is possible that the profile server's view of the neighborhood gets out of sync 
due to enforcement of various policies that the profile server implements when it communicates with servers in its neighborhood. For example, if a remote 
profile server violates the protocol, or in case the profile server finds out that the remote server's profile database is out of sync, the profile server 
may delete the remote server from its list of neighbors and thus force resynchronization of their databases. This is why this component periodically asks 
the LOC server to provide a fresh data, so that cancelled relationships with profile server's neighbors can be reestablished.


### Neighborhood Action Processor

Events related to the profile server's neighborhood, such as if a server joins or leaves the neighborhood, or a new identity becomes hosted, or an identity cancels its 
hosting with its profile server, do require an action to be performed by the profile server towards its neighbors or followers. The neighborhood action processor 
is the module that consumes such events from the database and executes the necessary actions in order to keep all related servers in sync.

The neighborhood action processor processes the actions in parallel, if possible. Two actions can be processed in parallel if they can not influence each other 
and their order of processing cannot affect their results. For example, if an identity changes its profile on the profile server, this change has to be propagated 
to all followers of the profile server. This creates as many neighborhood actions in the database as there are followers. All these actions can be processed in parallel 
because they target different servers. However, if the same identity changes its profile again, an action that propagates this second change to a follower 
must not be processed in parallel with the action that propagates the first change of this profile to the same follower.


### Outgoing Client

Outgoing client represents a connection that the profile server establishes in the role of a TCP client with another server. It is mostly used to contact 
other profile servers that live in the neighborhood of the profile server.



## Content Address Network Protocol

### Content Address Network Component

CAN network can be used to store data that can later be found by their identifiers. In the IoP network, client applications do not have writing access to CAN network 
and have to ask their profile servers to save data on their behalf. 

As the identifiers of the stored data are calculated from the binary forms of the stored data, it would not be possible to find data that belong to a certain identity 
without knowing the identifier or the data. To solve this problem there is a system of so called IPNS records, that allows finding stored data that belong 
to a certain user, just by knowing the identity of the user (its network identifier).

Profile server uses the IPNS system to store its contact information to the CAN network. This allows users of IoP network to find the contact information to 
a profile server just by knowing its network identifier. When a contact information of a profile server changes (e.g. its primary port is changed by its administrator) 
the profile server updates its IPNS record.

Similarly to this mechanism, the profile server submits IPNS records of its hosted identities that want to store their data to CAN network. Any other user of 
the network can then find these data provided that they know the network identifiers of the data owners. And again, if the user changes its data in CAN, the profile 
server updates its IPNS record.

IPNS records in CAN network expire in time and it is necessary for each IPNS record to be refreshed once in a while. It is the responsibility of this component 
to refresh the IPNS record of the profile server when it is needed. Refreshing IPNS records of hosted identities has to be initiated by themselves. The clients 
have to contact their profile servers and ask them to refresh their IPNS records before they expire in CAN network.



---
[Profile Server Data Layer](ARCH-PS-Data-Layer.md) « [Index](ARCHITECTURE.md) » [Profile Server Utility Layer](ARCH-PS-Utility-Layer.md)
