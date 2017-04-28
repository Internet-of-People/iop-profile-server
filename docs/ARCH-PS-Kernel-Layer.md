# Profile Server Kernel Layer

Kernel layer is where the execution of the profile server starts. The very first step is creating all the profile server's components, 
which are then passed to the component manager module, which is provided by IoP Server Library and does most of the work. 


## Kernel Module

The role of the kernel module is to create all components of the profile server and provide their correcly ordered list to the component manager.


## Configuration Component

The configuration component is the first component to be initialized during the profile server's startup. It loads the configuration 
file and checks the configuration settings in it. 

Then it loads the configuration from the database or initializes the configuration in the database. This part of the configuration 
includes the profile server's identity (i.e. its cryptographic keys). First time the profile server is started, its identity is generated.

The loaded configuration from both the configuration file and the database is then available to all other components.


---
[Profile Server Component Layers](ARCH-PS-Component-Layers.md) « [Index](ARCHITECTURE.md) » [Profile Server Data Layer](ARCH-PS-Data-Layer.md)
