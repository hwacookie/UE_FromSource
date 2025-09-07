"# UE_FromSource" 

This repository contains documentation, scripts and tools to help with building Unreal Engine from source code.

Please have a look at the `docs` folder for more information.

It also contains a tool that checks if all required tools are installed on your system, and creates a batch file to to package a UE4 project for distribution.

In order to build this tool, you need to have .NET SDK installed. You can download it from https://dotnet.microsoft.com/download. After that, run the following commands in your terminal:

```bash
dotnet build CheckTools.csproj
```
After that, you can run the tool using:

```bash
checktools.exe <path_to_your_ue4_project> <target_directory>
```

Example:

```bash 
checktools.exe "P:\prj\MyUE4Project" "P:\Temp\MyUE4Project\Packaged"
```
