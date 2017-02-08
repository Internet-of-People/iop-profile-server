# Profile Server Utility Layer

This layer contains various helper modules that are used by other components and modules across all layers. 
Most of the modules here are very simple modules to speed up a development or make the code more readable 
or faster to write. Those modules are not described in detail below.


## Logger

Extensive logging is implemented in all parts of the profile server as a way to make finding problems as quickly as possible.
This module implements a logging wrapper that allows prefixing logging messages with a fixed string.
It also creates a logging wrapper that connects the Entity Framework database inbuilt logging with the NLog logging, 
which allows us to log database queries.


## RegexUtils

This module implements the RegexType as it is defined in [Profile Server Network Protocol](https://github.com/Internet-of-People/message-protocol/blob/master/IopProfileServer.proto).
It is a limited regular expression that is used in search queries for profiles in the profile server network. 
The implementation in this module makes sure the regular expression is valid according to the protocol definition 
and it also imposes time constraints on the execution of a regular expression, so that the profile server is protected 
against sophisticated constructions of malicious regular expressions that could take too long to evaluate.




---
[Profile Server Network Layer](ARCH-PS-Network-Layer.md) « [Index](ARCHITECTURE.md) » [x](xxx.md)
