# Profile Server Data Layer

Profile server distinguishes between profile images and all other data. Images are stored on the disk in the images folder, separately from all other data,
which is stored in the database.


## Database 

Profile server uses Entity Framework Core ORM on SQLite database with code-first approach and repository and unit of work patterns.


### Database Models and Repositories

Each database table is represented by its model class and it's accessed through its repository. The following database tables are used:

 * Settings - Stores part of the configuration of the profile server including the profile server cryptographic identity.
 * Identities - Stores all identities that are hosted on this profile server. All identity profile data are stored in this table 
except for the profile images that are stored outside the database.
 * NeighborIdentities - Stores all identities that are hosted on neighbor profile servers. All identity profile data are stored in this table 
except for the thumbnail image that is stored outside the database.
 * RelatedIdentities - Stores information about relationships between profiles. These relationships are announced by the identities themselves, but they have to be cryptographically proved to be accepted.
 * NeighborhoodActions - Lists of pending actions related to the management of the profile server's neighborhood are stored in this table. When a profile server is informed about a change 
in its neighborhood, such as if a new server joined the neighborhood, or a server left the neighborhood, a new action is added. Similarly, in case of a change in the profile server's hosted 
profiles database, the change has to be propagated to the followers, which is done through actions in this table.
 * Neighbors - Stores a list of profile servers that the profile server considers to be its neighbors, i.e. updates of their hosted profiles can come from them.
 * Followers - Stores a list of profile servers that are following the profile server, i.e. changes in hosted profiles has to be propagated to them.


### Database Component

The database component cares about maintaining the database in a good shape, which means that it performs a number of cleanup tasks 
both during the startup as well as periodically during the normal life of the profile server. These tasks remove unused records from 
the database which were not removed as a part of normal execution flow for performance or practical reasons.



## Image Manager Component

The role of the image manager component is to save and load image data from the disk as well as offer image processing functionality 
to other components. The images folder contains all profile images in a 3-layer directory structure. Each image is represented 
by a file, which name is a SHA256 hash of the file contents. The first two layers are directory layers that form a prefix tree 
hierarchy for the files that are stored in the third layer.

The image manager maintains a reference counter to each image file and is thus able to recognize if a file is no longer 
in used and can be deleted from the disk.


---
[Profile Server Kernel Layer](ARCH-PS-Kernel-Layer.md) « [Index](ARCHITECTURE.md) » [Profile Server Network Layer](ARCH-PS-Network-Layer.md)
