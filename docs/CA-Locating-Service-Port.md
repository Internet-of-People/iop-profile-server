# Locating Service Port

## Problem

As a client that wants to communicate with a profile server, you need to be able to find its contact information - i.e. its IP address and port. 
We already mentioned that most commonly, clients use LOC servers to obtain the contact information of a profile server for the first time. 

The contact information one gets from LOC server refers to the profile server's primary interface. Let's say the client is interested 
to communicate with the profile server over its non customer interface. The client is expected to connect to this port and send `ListRolesRequest` 
message to obtain a mapping of server roles to ports, which includes information about the non customer interface. The client can now 
connect to this interface.

Let's assume that after some time the client wants to communicate with the same profile server again. Over time the profile server's 
configuration may change and the port previously used for the non customer interface might not be open anymore. It is even possible 
that the profile server's primary interface port is different and thus the client will be unable to find a contact point at all.


## Solution

The recommended practice for the client here is to save as much information about the profile server as possible and reuse that information later 
if it is expected that the client will need to communicate with the same profile server again in the future.
The first thing to save is the primary contact information received from the LOC server. This information will also include the network 
identifier of the profile server in `ServiceInfo.serviceData` field (see [Location Based Network Protocol](https://github.com/Internet-of-People/message-protocol/blob/master/IopLocNet.proto) for more).
If the contact information to the profile server was not obtained from a LOC server and the client does not know the network identifier 
of the profile server, it should initiate a conversation with the profile server using `StartConversationRequest` in order to get the server's public key 
in the response and this will allow the client to calculate the network identifier as the network identfiers are equal to `SHA256(publicKey)`.

Once a client obtains the mapping of server roles to TCP ports from the server using `ListRolesRequest`, it should save the whole information
for later use again.

If the client later fails to connect to the profile server's non customer port, the first thing to resolve the situation should be to 
try to connect to its primary port and use `ListRolesRequest` again to get the current port mapping information. 

If the connection to the primary port fails as well, this can be either because the primary port has changed, or that profile server is offline 
for whatever reason. To resolve this, the client should use CAN network to obtain the up to date contact information. 
Each profile server publishes its contact information to CAN network and creates an IPNS record under its network identifier
so that everyone knowing its identifier can find its latest contact information. The client thus needs to find a CAN server and resolve 
the profile server's network identifier. If the contact information from CAN contains the same IP address and primary port that the client failed to 
connect to, it means the profile server is offline and there is no way to communicate with it at the moment. Otherwise, the client now has a new contact 
information for the primary interface of the profile server and can attempt to contact it there.


---
[Setting Up Development Environment](CA-Setting-Up-Development-Environment.md) « [Index](CLIENT-APPS.md) » [Tests and Logs](CA-Tests-Logs.md)
