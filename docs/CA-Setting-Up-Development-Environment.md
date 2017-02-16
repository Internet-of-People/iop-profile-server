# Setting Up Development Environment

## Installation

To setup a local development environment, you are going to follow most of the steps of the standard [installation procedure](INSTALLATION.md).

What is different for the development environment is the configuration of the profile server.
In order to use a localhost interface, you will have to set the following settings in the configuration file:

`test_mode = on`
`server_interface = 127.0.0.1`

Without enabling the test mode, the profile server will not allow you to set the interface to IP address 127.0.0.1 or any other local network IP address.
Note that the test mode relaxes some checks of settings in the configuration file, so be careful when changing other settings.

Before you start the profile server, consider making a backup of its empty database file, so that you can revert to an empty database easily any time you need it.
Similarly, you can backup the database file at any point during your work if you consider that state of profile server to be something you might like to 
revert to at some point in the future.


## CAN and LOC Dependencies

Although profile server in the production environment closely relies on having CAN and LOC servers running on the same machine, 
the profile server will run just fine if one or both of those servers are not present when the profile server starts.
Profile server will periodically attempt to connect to both servers, but will continue to work even without their support. 
However, some features may not be available if one or both of those servers are not present. 


## Multiple Profile Servers Instances

It is perfectly fine to run two or more profile server instances on a same machine. You only need to make sure that each server 
has its own directory with its own database and configuration and you have to make sure that there are no port collisions 
among any two instances in their configuration files - i.e. a single port number can only be used in configuration file of one instance 
and must not be used for anything else in configuration files of other instances.

Note that if you run multiple instances on a same machine, they will not know about each other and they will not form a neighborhood relation.
Such a setup is more complicated and would need a LOC server to be involved. As a client application developer, you should not need such a setup, however.


---
[Protocol Basics](CA-Protocol-Basics.md) « [Index](CLIENT-APPS.md) » [Locating Service Port](CA-Locating-Service-Port.md)
