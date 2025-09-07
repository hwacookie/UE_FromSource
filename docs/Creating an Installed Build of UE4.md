# **Creating an Installed Build of the  UE4.27 Oculus Fork**

This document provides the final, working command and the required file modifications to create an installed build of the Oculus fork of Unreal Engine 4.27 using Visual Studio 2022\.

### **1\. File Modifications**

Two files needed to be created or modified to support Visual Studio 2022\.

#### **A. Create BuildConfiguration.xml**

This file tells the Unreal Build Tool to use your installed version of Visual Studio 2022, overriding any older defaults.

**File Path:** C:\\Users\\Hauke\\AppData\\Roaming\\Unreal Engine\\UnrealBuildTool\\BuildConfiguration.xml

**Content:**

\<?xml version="1.0" encoding="utf-8" ?\>  
\<Configuration xmlns="https://www.unrealengine.com/BuildConfiguration"\>  
    \<WindowsPlatform\>  
        \<Compiler\>VisualStudio2022\</Compiler\>  
    \</WindowsPlatform\>  
\</Configuration\>

#### **B. Modify InstalledEngineBuild.xml**

This modification tells the build script itself to use the \-2022 command-line argument when compiling, which is necessary for this specific engine fork.

**File Path:** P:\\prj\\UE427\_src\\UnrealEngine\\Engine\\Build\\InstalledEngineBuild.xml

Change:  
Find the section for VSCompilerArg and add the line for VS 2022\.  
**Before:**

\<\!-- Compile flags for Windows targets \--\>  
\<Property Name="VSCompilerArg" Value="-2017"/\>  
\<Property Name="VSCompilerArg" Value="-2019" If="$(VS2019)"/\>

**After:**

\<\!-- Compile flags for Windows targets \--\>  
\<Property Name="VSCompilerArg" Value="-2017"/\>  
\<Property Name="VSCompilerArg" Value="-2019" If="\$(VS2019)"/\>  
\<Property Name="VSCompilerArg" Value="-2022" If="\$(VS2022)"/\>  


### **2\. Final RunUAT Command**

This command will start the build process for Windows, Linux, and Android, and place the output in the specified directory.


Engine\\Build\\BatchFiles\\RunUAT.bat BuildGraph \-script="Engine/Build/InstalledEngineBuild.xml" \-target="Make Installed Build Win64" \-set:WithWin64=true \-set:WithLinux=true \-set:WithAndroid=true \-set:WithMac=false \-set:WithIOS=false \-set:WithTVOS=false \-set:WithHoloLens=false \-Install="P:\\temp\\UE4\_27\_Installed"

Run this command in a Visual Studio 2022 Developer Command Prompt. It takes a while to complete, but it will eventually create a fully installed build of the engine in the specified directory.

