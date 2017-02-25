# IoP Profile Server - Installation

In order to install and run the Profile Server, you need to 

 * Install .NET Core,
 * install OpenSSL (needed only for TLS certificate generation),
 * download the Profile Server source codes from GitHub,
   * AND build the Profile Server binaries,
 * OR download the Profile Server binaries and install dependencies,
 * configure the Profile Server,
 * run the Profile Server.


## Install .NET Core

Simply go to [Microsoft .NET Core website](https://www.microsoft.com/net/core) and follow the instructions on how to install .NET Core to your system.


## Install OpenSSL

Please visit [OpenSSL website](https://www.openssl.org/) and follow the instructions there. If you are running Windows OS, you can download OpenSSL from 
[Win32 OpenSSL Installation Project website](https://slproweb.com/products/Win32OpenSSL.html).


## Download Profile Server Source Codes

If you are familiar with GIT and GitHub, you will probably know what to do.
If you are not familiar with them, simply go to the [Main Page](https://github.com/Fermat-ORG/iop-profile-server/) of the repository and click the *Clone or download* green button 
on the right side. Then click the *Download ZIP* link and save the file to your disk. Unzip the file to any folder of your choice. This folder will be called `$InstDir` in the text below.


## Build Profile Server

Go to `$InstDir\src\ProfileServerCrypto` and execute 

```
dotnet restore
```

Go to `$InstDir\src\ProfileServerProtocol` and execute 

```
dotnet restore
```

Go to `$InstDir\src\ProfileServer` and execute 

```
dotnet restore --configfile NuGet.Config
dotnet build --configuration Release
```

The last command will create `$InstDir\src\ProfileServer\bin` a folder with a subfolder that contains the compiled Profile Server binaries. The actual name of the final binary folder 
differs with each operating system. The final folder with the binaries will be called `$BinDir` in the text below. 

Finally, go to `$InstDir\src\ProfileServer` and execute 

```
dotnet ef --configuration Release database update
```

to initialize the Profile Server's database file `ProfileServer.db`, which should be created in `$BinDir`.


## Download Profile Server Binaries

Go to [Releases Page](https://github.com/Fermat-ORG/iop-profile-server/releases) and download the latest release for your platform, if available.


### Dependencies for Windows 

Make sure your system is fully updated using Windows Update. Then you need to install [Visual C++ Redistributable for Visual Studio 2015 (64-bit)](https://www.microsoft.com/en-gb/download/details.aspx?id=48145).


### Dependencies for Linux

You need to have `libunwind` installed. If you do not have it, install it with the following command:

`apt-get install libunwind8`



## Configure Profile Server

### Generate TLS Certificate
You will need to generate a TLS certificate in PFX format and then modify the configuration file.

To generate a PFX certificate, you can use OpenSSL as follows. Make sure that the final certificate is NOT password protected.
```
openssl req -x509 -newkey rsa:4096 -keyout ProfileServer.key -out ProfileServer.cer -days 365000
openssl pkcs12 -export -out ProfileServer.pfx -inkey ProfileServer.key -in ProfileServer.cer
```

The new file `ProfileServer.pfx` is your TLS certificate that you need to put it in `$BinDir`.


### Modify Configuration File

Next step is to find the configuration file `$InstDir\src\ProfileServer\ProfileServer.conf` and copy it to the `$BinDir`. Then you have to modify it.
If you want to use the Profile Server in its default configuration, there is only one setting that you need to modify - `server_interface`. 
You have to set its value to the static public IP address of your server. For example, if your server's IP address is `198.51.100.53`, change the relevant line of the configuration file as follows:

```
server_interface = 198.51.100.53
```


### Add Logging Configuration

Copy logging configuration file `$InstDir\src\ProfileServer\Nlog.conf` to your `$BinDir`.



## Run Profile Server

There are two ways how to run the Profile Server. If your system is one of the supported system, on which the build process generated executable files, or you were able to download Profile Server binaries, 
you simply go to your `$BinDir` and execute:

```
ProfileServer
```

Otherwise, you can go to `$InstDir\src\ProfileServer` and execute

```
dotnet run
```

but in this case your execution directory is going to be `$InstDir\src\ProfileServer`, which means you will have to copy the TLS certificate PFX file and the configuration files to this directory and also you 
will have to copy the database file `ProfileServer.db` to this directory. 


## Troubleshooting

If you added the logging configuration file to your `$BinDir` as described above, every time you run the Profile Server, logs are going to be created in `$BinDir\Logs` folder. If there are any problems 
with your Profile Server, the log file will contain detailed information about it and may help you solve the problems.


