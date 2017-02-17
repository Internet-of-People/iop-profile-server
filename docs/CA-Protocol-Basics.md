# Protocol Basics

## Prerequisites

We assume that you are familiar with [Profile Server Architecture](ARCHITECTURE.md). If not, please read it first. 
You should also be familiar with profile server network protocol, which is described in its [.proto file](https://github.com/Internet-of-People/message-protocol/blob/master/IopProfileServer.proto).
We are going to explain the protocol in greater details in this introduction.


## Error Handling

Status codes (or error codes) are the first thing defined in the protocol definition file. In profile server network, every peer is expected to be very strict in its error handling. 
As a client, you should always check the returned status code and implement a proper handling of every possible code that you can receive. 

There are two groups of status codes that you can receive in a response to a request:

 * general status codes,
 * request specific status codes.

General status codes are related to the protocol itself or the status of the profile server that you are communicating with. 
A good example of an error that returns a general status code is a protocol violation error. Any request can result in a general 
status code being returned.

Request specific codes are error results that can only be returned by requests that explicitly declare them as their possible response status codes.
For example, a profile server can reply with `ERROR_INVALID_SIGNATURE` or `ERROR_INVALID_VALUE` to `VerifyIdentityRequest` message
but not with any other request specific status code.

Besides returning an error code to a client, the profile server can also terminate the conection. This is done only if necessary 
and it usually means that the profile server does not consider it possible to recover from the error. This is mostly the case 
of protocol violation errors, but being banned is another case in which disconnecting makes sense. Most of the errors will not cause the connection 
to be terminated.


## Request - Response

Each message is either a request or a response to a request that was previously sent within the same connection. Each request 
comes with a unique message identifier and the response repeats the identifier so that the requestor can match the incoming response 
to its corresponding request. The requestor is the one responsible for the uniqueness of request identifiers. The requestee 
may not (and if the profile server is the requestee, it does not) check it and if a duplicate identifier is used the behavior
is undefined.

It is possible to send another request before previous requests are processed and produced responses. The order of the responses 
is not guranteed if there are multiple unfinished requests. The message identifier in the response must be used to recognize 
the corresponding request.

Note that not always the client application is the side to send request and the profile server is the side to send response. 
The most obvious examples of inverse message flows are certain messages related to application service calls. In general, any kind 
of server notification usually results in server sending a notification request and a client replying with a reponse.



## Server Roles

Profile servers has different roles, which can be served on different TCP ports. When you obtain a contact information (IP address and port) 
it almost always refers to the profile server's primary interface. Most of the time, you will receive contact to a profile server from a LOC server. 
If this is what you want to achieve, please see its documentation.

You can use the primary interface (via `ListRolesRequest` message) to obtain a list of roles with their assigned ports. 
As a client, you may be interested either in non customer client interface or customer client interface. 
If your client does not represent an identity hosted on the profile server you are communicating with, you are going to use non customer client.
Otherwise, you will use customer client interace.



## Conversations

As you know from the protocol definition file, each request is either a single request without further context, 
or it is a part of the conversation, which is a series of requests with a common context. In order to communicate within a conversation, 
your client needs to represent an identity. Each pair of Ed25519 keys represents an identity in profile server's protocol. 
You should never be required to disclose the private key to any peer in the network.

During the conversation setup (using `StartConversationRequest`), the requestor (usually the client application) provides a challenge to 
be signed by the requestee (usually the profile server) and the signature is delivered in the response together with the requestee's identity 
(a public key). As a client, you should always verify that the presented signature corresponds the claimed identity. Also, if you know 
the profile server network identifier from another source (e.g. LOC server), or you had a conversation with the same profile server before, 
you should verify that its identity matches the expected value and has not changed.

Note that single requests that can be used on customer client interface are not limited by the conversation status - i.e. they can be sent 
without going through the authentication process.



---
[Index](CLIENT-APPS.md) » [Setting Up Development Environment](CA-Setting-Up-Development-Environment.md)
