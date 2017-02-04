# IoP Profile Server - Installation

In order to install and run Profile Server, you need to 

 * Install .NET Core,
 * install OpenSSL (needed only for TLS certificate generation),
 * download the Profile Server source codes from GitHub,
   * AND build the Profile Server binaries,
 * OR download the Profile Server binaries,
 * configure the Profile Server,
 * run the Profile Server.


## Install .NET Core

Simply go to [Microsoft .NET Core website](https://www.microsoft.com/net/core) and follow the instruction on how to install .NET Core to your system.


## Install OpenSSL

Please visit [OpenSSL website](https://www.openssl.org/) and follow the instructions there. If you are running Windows OS, you can download OpenSSL from 
[Win32 OpenSSL Installation Project website](https://slproweb.com/products/Win32OpenSSL.html).


## Download Profile Server Source Codes

If you are familiar with GIT and GitHub, you will probably know what to do.
If you are not familiar with it, simply go to the [Main Page](https://github.com/Fermat-ORG/iop-profile-server/) of the repository and click the *Clone and download* green button 
on the right side. Then click the *Download ZIP* link and save the file on your disk. Unzip the file to any folder of your choice. This folder will be called `$InstDir` in the text below.


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
dotnet restore
dotnet build
```

The last command will create `$InstDir\src\ProfileServer\bin` folder with a subfolder that contains the compiled Profile Server binaries. The actual name of the final binary folder 
differs with each operating system. The final folder with the binaries will be called `$BinDir` in the text below. 

Finally, go to `$InstDir\src\ProfileServer` and execute 

```
dotnet ef database update
```

to initialize the Profile Server's database.


## Download Profile Server Binaries

Go to [Releases Page](https://github.com/Fermat-ORG/iop-profile-server/releases) and download the latest release for your platform, if available.


## Configure Profile Server

### Generate TLS Certificate
You will need to generate a TLS certificate and then modify the configuration file.

To generate PFX certificate, you can use OpenSSL as follows. Make sure that the final certificate is NOT password protected.
```
openssl req -x509 -newkey rsa:4096 -keyout ProfileServer.key -out ProfileServer.cer -days 365000
openssl pkcs12 -export -out ProfileServer.pfx -inkey ProfileServer.key -in ProfileServer.cer
```

The new file `ProfileServer.pfx` is your TLS certificate that you need to put it in the `$BinDir`.


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
you simply got to your `$BinDir` and execute:

```
ProfileServer
```

Otherwise, you can go to `$InstDir\src\ProfileServer` and execute

```
dotnet run
```


## Troubleshooting

If you added a logging configuration file to your `$BinDir` as described above, every time you run the Profile Server, logs are going to be created in `$BinDir\Logs` folder. If there are any problems 
with your Profile Server, the log file will contain detailed information about it and may help you solve the problems.


