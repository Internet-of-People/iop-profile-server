# Profile Server Fundamentals

## Introduction

Profile server is a TCP network server that implements services defined by [IoP Profile Server Protocol](https://github.com/Internet-of-People/message-protocol).
This particular implementation is a fully asynchronous, multithreaded server written in C# and runs on [.NET Core platform](https://www.microsoft.com/net/core) 
and the further text is related to this implementation only and it may not be accurate for other implementations of IoP Profile Servers.


## Cryptography 

Profile server uses [Ed25519](http://ed25519.cr.yp.to/) signature system for the representation of identities, this implementation is based on [Chaos.NaCl](https://github.com/CodesInChaos/Chaos.NaCl/) library. 
For encryption of data transferred over the network, we use [TLS 1.2](https://en.wikipedia.org/wiki/Transport_Layer_Security#TLS_1.2), which is built in .NET Core.

Each identity in the IoP network is represented by a single Ed25519 key pair. A network identifier of an identity is then a SHA256 hash of its public key.


## Serialization Protocol

IoP Profile Server Protocol uses [Google Protobuf v3](https://developers.google.com/protocol-buffers/docs/proto3) as a serialization mechanism. 
Profile server uses [Google Protocol Buffers library](https://www.nuget.org/packages/Google.Protobuf/) to handle the Protobuf serialization.


## Database

Profile server currently uses [Entity Framework Core](https://docs.microsoft.com/en-us/ef/core/index) on SQLite 3 database. In the future it might be 
replaced with a fully mature database engine due to performance reasons. As of now the database performance is not an issue.


## Logging

We implement extensive logging in all profile server code, for which we use [NLog library](http://nlog-project.org/).


## Projects

The development of the profile server consists of several projects. Besides the main profile server project, there are two projects that the profile server project
itself depends on:

 * Profile Server Crypto library which implements the Ed25519 cryptographic subsystem.
 * Profile Server Protocol library which implements IoP Network Protocol. 

Besides that there are projects to support the development:

 * Unit Tests project simply implements unit tests for profile server code where needed.
 * Profile Server Protocol Tests is a project that contains all functional IoP Network protocol tests related to the profile server, but the implementation is independent of a specific profile server implementation. 
These tests verify that a particular implementation of the profile server complies with the protocol specification.
 * Profile Server Protocol Tests Executor project is a tool that allows easy batch execution of the tests from the Profile Server Protocol Tests project.
 * Profile Server Network Simulator project is a tool with which we can simulate a network of profile servers and execute various scenarios and verify the correctness of each server's behavior.

The further documentation in this overview focuses on the main Profile Server project.


---
[Profile Server in IoP Network](ARCH-PS-in-IoP.md) « [Index](ARCHITECTURE.md) » [Profile Server Component Layers](ARCH-PS-Component-Layers.md)