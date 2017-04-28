# Profile Server Component Layers

Each of the profile server's module belong to one of the three layers - kernel layer, data layer, and network layer.
In the profile server's design a *component* is a special module that is controlled by the component manager and has a controlled life cycle. 

## IoP Server Library

Besides the layers in the profile server itself, there is a special layer in form of IoP Server Library.
This libary contains modules across all layers that can be reused in different projects. This includes:

 * Modules related to logging, regular expression evaluation, file handling, and extension classes module.
 * Module providing cryptography functions.
 * Set of modules implementing the IoP network protocol and providing support for easy work with the protocol on higher levels.
 * Set of modules collectively known as *Server Core*, which provide base classes and support for building IoP network servers, including 
   * modules that define component structure and implement component life cycle management;
   * cron component that is responsible for running repeated tasks;
   * TCP role server module that represents a single open port which offers services of one or more server's interfaces;
   * base classes for implementation of network clients.


## Kernel Layer

Kernel layer contains the following:

 * Kernel module that cares about correct initialization of all other components.
 * Configuration component that loads and stores the profile server configuration from the configuration file.


## Data Layer

Profile server stores all of its data to a database, except for images of identity profiles that are stored in separate files on disk. 
Data layer contains:

 * Modules related to the database access and its structure.
 * Database component that is responsible for initialization of the database during the startup and database cleanup tasks.
 * Image manager component that cares about loading and storing images as well as image processing.


## Network Layer

The largest layer in profile server is the network layer, it consists of the following components and modules:

 * Network server component that creates and manages all running TCP role servers from IoP Server Library.
 * Incoming client module that represents an incoming TCP connection to the TCP role server.
 * Message processor module that processes messages from incoming clients.
 * Location based network component that implements communication with Location Based Network server.
 * Content address network component that implements communication with Content Address Network server.
 * Neighborhood action processor component which is responsible for handling events related to profile server neighborhood interactions.
 * Other network related modules, such as modules representing outgoing network clients, modules related to application service calls functionality etc.


---
*Components and Layers (click on the image and then click Download to see it in full size)*

![Profile Servers components and layers](images/ps-component-layers.png "Profile Servers components and layers")

---
[Profile Server Fundamentals](ARCH-PS-Fundamentals.md) « [Index](ARCHITECTURE.md) » [Profile Server Kernel Layer](ARCH-PS-Kernel-Layer.md)
