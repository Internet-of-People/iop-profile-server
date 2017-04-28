# Profile Server Fundamentals

## Introduction

Profile server is a TCP network server that implements services defined by [IoP Profile Server Protocol](https://github.com/Internet-of-People/message-protocol).
This particular implementation is a fully asynchronous, multithreaded server written in C# and runs on [.NET Core platform](https://www.microsoft.com/net/core) 
and the further text is related to this implementation only and it may not be accurate for other implementations of IoP Profile Server.


## IoP Server Library

Profile server depends on [IoP Server Library](https://github.com/Fermat-ORG/iop-server-library), which is a set of reusable modules that can be used 
to create IoP network servers as well as clients that connect to them. The following are some examples of what IoP Server Library offers:


### Cryptography 

Profile server uses [Ed25519](http://ed25519.cr.yp.to/) signature system for the representation of identities, this implementation is based on [Chaos.NaCl](https://github.com/CodesInChaos/Chaos.NaCl/) library. 
For encryption of data transferred over the network, we use [TLS 1.2](https://en.wikipedia.org/wiki/Transport_Layer_Security#TLS_1.2), which is built in .NET Core.

Each identity in the IoP network is represented by a single Ed25519 key pair. A network identifier of an identity is then a SHA256 hash of its public key.


### Serialization Protocol

IoP Profile Server Protocol uses [Google Protobuf v3](https://developers.google.com/protocol-buffers/docs/proto3) as a serialization mechanism. 
Profile server uses [Google Protocol Buffers library](https://www.nuget.org/packages/Google.Protobuf/) to handle the Protobuf serialization.


### Logging

We implement extensive logging in all profile server code, for which we use [NLog library](http://nlog-project.org/).



## Database

Profile server currently uses [Entity Framework Core](https://docs.microsoft.com/en-us/ef/core/index) on SQLite 3 database. In the future it might be 
replaced with a fully mature database engine due to performance reasons. As of now the database performance is not an issue.




## Projects

The development of the profile server consists of several projects. The main profile server project only depends on the IoP Server Library, but there are other project 
that support the development:

 * [IoP Message Protocol Tests](https://github.com/Fermat-ORG/iop-message-protocol-tests) is a project that contains all functional IoP Network protocol tests related 
to the profile server, but the implementation is independent from a specific profile server implementation. These tests verify that a particular implementation of 
the profile server conforms to the protocol specification.
 * [IoP Message Protocol Tests Executor](https://github.com/Fermat-ORG/iop-message-protocol-tests-executor) project is a tool that allows easy batch execution of the tests from the IoP Message Protocol Tests project.
 * [IoP Network Simulator](https://github.com/Fermat-ORG/iop-network-simulator) project is a tool with which we can simulate a network of servers and execute various scenarios and verify the correctness of each server's behavior.

The further documentation in this overview focuses on the main profile server project.


---
[Profile Server in IoP Network](ARCH-PS-in-IoP.md) « [Index](ARCHITECTURE.md) » [Profile Server Component Layers](ARCH-PS-Component-Layers.md)
