# Profile Server Component Layers

Each of the profile server's components belongs to one of the four layers - kernel layer, data layer, network layer, and utility layer.
In profile server's design a *component* is a special module that is controlled by the component manager and has a controlled life cycle. 


## Kernel Layer

Kernel layer contains the following:

 * Modules that define component structure, their life cycle management.
 * Configuration component that loads ands stores the profile server configuration from the configuration file.
 * Cron component that is responsible for running repeated tasks.


## Data Layer

Profile server stores most of its data to a database, but images of identity profiles are stored in separate files on disk. 
Data layer thus contains:

 * Modules related to database access and structure.
 * Database component that is responsible for initialization of the database during the startup and cleanup during shutdown.
 * Image manager component that cares about loading and storing images as well as image processing.


## Network Layer

The largest layer in profile server is the network layer, it consists of the following components and modules:

 * Network server component that manages all running TCP role servers.
 * TCP role server module that represents a single open port which offers services of one or more profile server's interfaces.
 * Message processor module that processes incoming messages to TCP role servers.
 * Location based network component that implements communication with Location Based Network server.
 * Content address network component that implements communication with Content Address Network server.
 * Neighborhood action processor component which is responsible for handling of events related to profile server neighborhood interactions.
 * Other network related module, such as modules represening incoming and outgoing network clients, modules related to application service calls functionality etc.


## Utility Layer

Utility layer consists of a bunch of helper modules that are used by different components across the layers.
Any module that does not fit into the first three layers goes here. Examples of components in the utility layer 
are modules related to logging, helper file handling modules, extension classes module etc.


---
*Components and Layers (click the image and then on download to see it in full size)*

![Profile Servers components and layers](images/ps-component-layers.png "Profile Servers components and layers")

---
[Profile Server Fundamentals](ARCH-PS-Fundamentals.md) « [Index](ARCHITECTURE.md) » [Profile Server Kernel Layer](ARCH-PS-Kernel-Layer.md)