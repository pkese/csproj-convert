
#### What

This project will automatically patch old style .csproj files into dotnet core compatible ones.

It is based primarily on the following post:  
http://www.natemcmaster.com/blog/2017/03/09/vs2015-to-vs2017-upgrade/

Conversion is not perfect and things may go wrong, but if you have a large project, with lots of .csproj files, it will save a lot of time.  
You have been warned. Keep your backups. *This code will eat kittens.*


#### How

    git clone <this-repository>

    cd CsprojConverter

    dotnet restore

    dotnet run analyze <path-to-solution-file> <path-to-nuget-packages-directory-for-that-project>

Then look at the generated ouptut and edit your `CustomConfig.fs` to make output match your expectations.  
Mostly you can ignore `Skipping ...` reports, as dotnet core will automatically include packages that are referenced by other packages.

When ready, just run the tool with `patch` flag and it will rewrite all `.csproj` files and delete all `packages.config` files.

    dotnet run patch <path-to-solution-file> <path-to-nuget-packages-for-that-project>


Before that it is wise to close Visual Studio and run your variant of `make clean`.

After patching, first do a `dotnet restore <solution-file>` before starting Visual Studio. If you are using Resharper, it is advised to temporarily disable it because it may eat all your CPU and IO when fiddling with the new code.

Things likely won't work perfectly. If some nuget references are missing or have wrong versions, then either
 - delete them completely (and add them back) in Visual Studio 
 - patch `CustomConfig.cs` and repeat: first check the `Skipping` output or look at the difference between old and new project file, see under which name old package used to be included to and add the new nuget style name and version to the `CustomConfig.cs`, then do a `git reset --hard` (or its svn counterpart), rinse and repeat.


#### Development

I hope you can edit this code in normal Visual Studio -- in my case VS was not happy with dotnet-core F# projects.  
So I'm happily using [Visual Studio Code](https://code.visualstudio.com/) with [Ionide](http://ionide.io/) plugin.  
[Atom](https://atom.io/) is also a good option, however Ionide's Atom support lags behind a little bit and doesn't provide all of its bells and whistles.

#### Community

I may not respond very promptly to Bug reports, but I will pull in patch requests from time to time.  
So just fork the code and commit your patches in to your own repo.