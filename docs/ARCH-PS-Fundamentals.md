# Profile Server Fundamentals

## Introduction

Profile server is a TCP network server that implements services defined by [IoP Profile Server Protocol](https://github.com/Internet-of-People/message-protocol).
This particular implementation is a fully asynchronous, multithreaded server written in C# and runs on [.NET Core platform](https://www.microsoft.com/net/core) 
and the further description related to this implementation and may not be accurate for other implementations of IoP Profile Servers.


## Cryptography 

Profile server uses [Ed25519](http://ed25519.cr.yp.to/) signature system for the representation of identities, this implementation is based on [Chaos.NaCl](https://github.com/CodesInChaos/Chaos.NaCl/) library. 
For encryption of data transferred over the network, we use [TLS 1.2](https://en.wikipedia.org/wiki/Transport_Layer_Security#TLS_1.2), which is built in .NET Core.


## Serialization Protocol

IoP Profile Server Protocol uses [Google Protobuf v3](https://developers.google.com/protocol-buffers/docs/proto3) as a serialization mechanism. 
Profile server uses [Google Protocol Buffers library](https://www.nuget.org/packages/Google.Protobuf/) to handle the Protobuf serialization.


## Database

Profile server currently uses [Entity Framework Core](https://docs.microsoft.com/en-us/ef/core/index) on SQLite 3 database. In the future it might be possible that 
this is replaced due to performance reasons for a fully mature database engine. As of now the database performance is not an issue.


## Logging

We implement extensive logging in all profile server code, for which we use [NLog library](http://nlog-project.org/).


---
[Profile Server in IoP Network](ARCH-PS-in-IoP.md) « [Index](ARCHITECTURE.md) » [xxx](ARCH-PS-Components.md)