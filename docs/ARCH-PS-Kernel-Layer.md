# Profile Server Kernel Layer

Kernel layer is where the execution of the profile server starts. The very first step is creating all profile server's components, 
which are then passed to the component manager module. 


## Component Manager

The role of the component manager is to manage the life cycle of other components. This is a very simple task that consists 
of calling the two methods that each component has to implement:

 * The initialization method that initializes everything the component need for doing its work. It is called during the profile server startup.
 * The shutdown method that terminates the execution of all parts of the component and frees resources used by the component. It is called during the termination of the profile server.

The component manager also manages a global shutdown signaling mechanism, which helps proper termination of each component during the shutdown.



## Configuration Component

The configuration component is the first component to be initialized during the profile server's startup. It loads the configuration 
file and check the configuration settings in it. 

Then it loads the configuration from the database, or initializes the configuration in the database. This part of configuration 
includes the profile server's identity (i.e. its cryptographic keys). First time the profile server is started, its identity is generated.

The loaded configuration from both the configuration file and the database is then available to all other components.



## Cron Component

This component is the last component that is initialized after all other components in the system are ready and running. 
Its only function is to periodically execute various tasks in other components.


---
[Profile Server Component Layers](ARCH-PS-Component-Layers.md) « [Index](ARCHITECTURE.md) » [Profile Server Data Layer](ARCH-PS-Data-Layer.md)