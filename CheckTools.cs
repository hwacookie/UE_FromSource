using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== Unreal Engine 4.27.2 Android Packaging Check (Oculus Fork) ===");

        // Check if project packaging is requested
        if (args.Length >= 2)
        {
            string uprojectPath = args[0];
            string outputPath = args[1];
            
            if (!File.Exists(uprojectPath))
            {
                Console.WriteLine($"ERROR: .uproject file not found: {uprojectPath}");
                return;
            }
            
            if (!uprojectPath.ToLower().EndsWith(".uproject"))
            {
                Console.WriteLine($"ERROR: File must be a .uproject file: {uprojectPath}");
                return;
            }
            
            if (!Directory.Exists(outputPath))
            {
                Console.WriteLine($"Creating output directory: {outputPath}");
                try
                {
                    Directory.CreateDirectory(outputPath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: Cannot create output directory: {ex.Message}");
                    return;
                }
            }
            
            Console.WriteLine($"Project file: {uprojectPath}");
            Console.WriteLine($"Output directory: {outputPath}");
            Console.WriteLine();
        }

        // Run toolchain checks
        bool allToolsReady = true;
        allToolsReady &= CheckDotNetFramework("4.6.2");
        allToolsReady &= CheckVisualStudio("2022");
        allToolsReady &= CheckAndroidStudio();
        allToolsReady &= CheckAndroidSDK();
        allToolsReady &= CheckAndroidNDK();
        allToolsReady &= CheckJavaJDK();
        allToolsReady &= CheckLinuxCrossCompileToolchain();
        allToolsReady &= CheckCMake();
        allToolsReady &= CheckMSBuild();
        allToolsReady &= CheckGit();
        CheckOculusIntegration(); // This is informational only

        Console.WriteLine("=== Check Complete ===");
        
        // Generate packaging command if parameters provided and tools are ready
        if (args.Length >= 2 && allToolsReady)
        {
            string platform = args.Length >= 3 ? args[2] : "Android";
            GeneratePackagingCommand(args[0], args[1], platform);
        }
        else if (args.Length >= 2 && !allToolsReady)
        {
            Console.WriteLine();
            Console.WriteLine("ERROR: Cannot generate packaging command - some required tools are missing!");
        }
        else if (args.Length == 0)
        {
            Console.WriteLine();
            Console.WriteLine("Usage: CheckTools.exe <path-to-uproject> <output-directory> [platform]");
            Console.WriteLine("  <path-to-uproject>  Path to your .uproject file");
            Console.WriteLine("  <output-directory>  Directory where packaged files will be stored");
            Console.WriteLine("  [platform]          Target platform: Android (default) or Linux");
        }
    }

    static bool CheckDotNetFramework(string version)
    {
        Console.Write($"Checking .NET Framework {version} Developer Pack... ");
        try
        {
            using (var ndpKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                .OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\"))
            {
                int releaseKey = (int)(ndpKey?.GetValue("Release") ?? 0);
                // 4.6.2 = 394802 or 394806
                if (releaseKey >= 394802)
                {
                    Console.WriteLine("OK");
                    return true;
                }
                else
                {
                    Console.WriteLine("NOT FOUND or too old!");
                    return false;
                }
            }
        }
        catch
        {
            Console.WriteLine("NOT FOUND!");
            return false;
        }
    }

    static bool CheckVisualStudio(string year)
    {
        Console.Write($"Checking Visual Studio {year}... ");
        string vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere))
        {
            Console.WriteLine("vswhere.exe not found!");
            return false;
        }
        
        // Use a broader search that checks for any version of the specified year
        string arguments = year == "2022" ? 
            "-version \"[17.0,18.0)\" -latest -property installationPath" :
            "-version \"[16.0,17.0)\" -latest -property installationPath";
            
        var psi = new ProcessStartInfo
        {
            FileName = vswhere,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        try
        {
            var proc = Process.Start(psi);
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            if (!string.IsNullOrWhiteSpace(output))
            {
                Console.WriteLine("OK (" + output.Trim() + ")");
                return true;
            }
            else
            {
                Console.WriteLine("NOT FOUND!");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return false;
        }
    }

    static bool CheckCMake()
    {
        Console.Write("Checking CMake... ");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmake",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            var proc = Process.Start(psi);
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            if (output.Contains("cmake version"))
            {
                string[] lines = output.Split('\n');
                string versionLine = lines[0].Trim();
                if (versionLine.Contains("cmake version"))
                {
                    string version = versionLine.Replace("cmake version", "").Trim();
                    Console.WriteLine("OK (version " + version + ")");
                }
                else
                {
                    Console.WriteLine("OK");
                }
                return true;
            }
            else
            {
                Console.WriteLine("NOT FOUND!");
                return false;
            }
        }
        catch
        {
            Console.WriteLine("NOT FOUND!");
            return false;
        }
    }

    static bool CheckMSBuild()
    {
        Console.Write("Checking MSBuild... ");
        
        // Try to find MSBuild through vswhere first
        string vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
            
        if (File.Exists(vswhere))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = vswhere,
                    Arguments = "-latest -products * -requires Microsoft.Component.MSBuild -property installationPath",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                var proc = Process.Start(psi);
                string vsPath = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                
                if (!string.IsNullOrWhiteSpace(vsPath))
                {
                    string msbuildPath = Path.Combine(vsPath, "MSBuild", "Current", "Bin", "MSBuild.exe");
                    if (!File.Exists(msbuildPath))
                    {
                        // Try older path structure
                        msbuildPath = Path.Combine(vsPath, "MSBuild", "15.0", "Bin", "MSBuild.exe");
                    }
                    
                    if (File.Exists(msbuildPath))
                    {
                        var msbuildPsi = new ProcessStartInfo
                        {
                            FileName = msbuildPath,
                            Arguments = "-version",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false
                        };
                        var msbuildProc = Process.Start(msbuildPsi);
                        string output = msbuildProc.StandardOutput.ReadToEnd();
                        msbuildProc.WaitForExit();
                        
                        if (output.ToLower().Contains("msbuild version") || output.ToLower().Contains("microsoft") && output.ToLower().Contains("build engine"))
                        {
                            string[] lines = output.Split('\n');
                            string versionInfo = lines[0].Trim();
                            if (!string.IsNullOrWhiteSpace(versionInfo))
                            {
                                Console.WriteLine("OK (" + versionInfo + ")");
                                return true;
                            }
                            Console.WriteLine("OK");
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Fall through to try msbuild in PATH
            }
        }
        
        // Fallback: try msbuild in PATH
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "msbuild",
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            var proc = Process.Start(psi);
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            if (output.ToLower().Contains("msbuild version") || output.ToLower().Contains("microsoft") && output.ToLower().Contains("build engine"))
            {
                string[] lines = output.Split('\n');
                string versionInfo = lines[0].Trim();
                if (!string.IsNullOrWhiteSpace(versionInfo))
                {
                    Console.WriteLine("OK (" + versionInfo + ")");
                    return true;
                }
                Console.WriteLine("OK");
                return true;
            }
            else
            {
                Console.WriteLine("NOT FOUND!");
                return false;
            }
        }
        catch
        {
            Console.WriteLine("NOT FOUND!");
            return false;
        }
    }

    static bool CheckGit()
    {
        Console.Write("Checking Git... ");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            var proc = Process.Start(psi);
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            if (output.ToLower().Contains("git version"))
            {
                Console.WriteLine("OK (" + output.Trim() + ")");
                return true;
            }
            else
            {
                Console.WriteLine("NOT FOUND!");
                return false;
            }
        }
        catch
        {
            Console.WriteLine("NOT FOUND!");
            return false;
        }
    }

    static bool CheckAndroidStudio()
    {
        Console.Write("Checking Android Studio... ");
        
        // Common Android Studio installation paths
        string[] possiblePaths = {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Android", "Android Studio", "bin", "studio64.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Android Studio", "bin", "studio64.exe"),
            Path.Combine("C:", "Program Files", "Android", "Android Studio", "bin", "studio64.exe")
        };

        foreach (string path in possiblePaths)
        {
            if (File.Exists(path))
            {
                Console.WriteLine("OK (" + Path.GetDirectoryName(Path.GetDirectoryName(path)) + ")");
                return true;
            }
        }
        Console.WriteLine("NOT FOUND!");
        return false;
    }

    static bool CheckAndroidSDK()
    {
        Console.Write("Checking Android SDK... ");
        
        // Check environment variables first
        string androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME");
        string androidSdkRoot = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        
        string sdkPath = androidHome ?? androidSdkRoot;
        
        if (string.IsNullOrEmpty(sdkPath))
        {
            // Try common locations
            string[] possiblePaths = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Android", "Sdk"),
                "C:\\Android\\Sdk"
            };
            
            foreach (string path in possiblePaths)
            {
                if (Directory.Exists(path) && File.Exists(Path.Combine(path, "platform-tools", "adb.exe")))
                {
                    sdkPath = path;
                    break;
                }
            }
        }
        
        if (!string.IsNullOrEmpty(sdkPath) && Directory.Exists(sdkPath))
        {
            // Check for required API levels (UE4.27.2 typically needs API 28-30)
            string platformsPath = Path.Combine(sdkPath, "platforms");
            if (Directory.Exists(platformsPath))
            {
                var platforms = Directory.GetDirectories(platformsPath)
                    .Select(d => Path.GetFileName(d))
                    .Where(name => name.StartsWith("android-"))
                    .ToArray();
                    
                if (platforms.Any(p => p == "android-28" || p == "android-29" || p == "android-30"))
                {
                    Console.WriteLine("OK (" + sdkPath + ")");
                    return true;
                }
            }
            Console.WriteLine("FOUND but missing required API levels (need API 28-30)");
            return false;
        }
        else
        {
            Console.WriteLine("NOT FOUND! Set ANDROID_HOME or ANDROID_SDK_ROOT environment variable");
            return false;
        }
    }

    static bool CheckAndroidNDK()
    {
        Console.Write("Checking Android NDK... ");
        
        string androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME");
        string androidSdkRoot = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        string ndkHome = Environment.GetEnvironmentVariable("ANDROID_NDK_HOME");
        
        string[] possiblePaths = new string[0];
        
        if (!string.IsNullOrEmpty(ndkHome))
        {
            possiblePaths = new[] { ndkHome };
        }
        else if (!string.IsNullOrEmpty(androidHome))
        {
            possiblePaths = new[] { Path.Combine(androidHome, "ndk-bundle"), Path.Combine(androidHome, "ndk") };
        }
        else if (!string.IsNullOrEmpty(androidSdkRoot))
        {
            possiblePaths = new[] { Path.Combine(androidSdkRoot, "ndk-bundle"), Path.Combine(androidSdkRoot, "ndk") };
        }
        
        // Add common fallback locations
        var fallbackPaths = new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "ndk-bundle"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "ndk"),
            "C:\\Android\\Sdk\\ndk-bundle",
            "C:\\Android\\Sdk\\ndk"
        };
        
        possiblePaths = possiblePaths.Concat(fallbackPaths).ToArray();
        
        foreach (string basePath in possiblePaths)
        {
            if (Directory.Exists(basePath))
            {
                // Check if it's a direct NDK path or contains versioned NDK folders
                if (File.Exists(Path.Combine(basePath, "ndk-build.cmd")))
                {
                    Console.WriteLine("OK (" + basePath + ")");
                    return true;
                }
                
                // Check for versioned NDK folders
                var ndkVersions = Directory.GetDirectories(basePath)
                    .Where(d => File.Exists(Path.Combine(d, "ndk-build.cmd")))
                    .ToArray();
                    
                if (ndkVersions.Any())
                {
                    Console.WriteLine("OK (" + ndkVersions.First() + ")");
                    return true;
                }
            }
        }
        
        Console.WriteLine("NOT FOUND! UE4.27.2 requires Android NDK r21b or compatible");
        return false;
    }

    static bool CheckLinuxCrossCompileToolchain()
    {
        Console.Write("Checking Linux Cross-Compilation Toolchain... ");
        
        // Check for UE4's Linux toolchain (clang-based)
        string[] possibleToolchainPaths = {
            // UE4.27 default toolchain paths
            "C:\\UnrealToolchains\\v19_clang-11.0.1-centos7",
            "C:\\UnrealToolchains\\v20_clang-13.0.1-centos7",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "UnrealToolchains", "v19_clang-11.0.1-centos7"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "UnrealToolchains", "v20_clang-13.0.1-centos7"),
            
            // Check environment variable
            Environment.GetEnvironmentVariable("LINUX_MULTIARCH_ROOT"),
            
            // Alternative locations
            "D:\\UnrealToolchains\\v19_clang-11.0.1-centos7",
            "D:\\UnrealToolchains\\v20_clang-13.0.1-centos7"
        };
        
        string foundToolchain = null;
        foreach (string path in possibleToolchainPaths)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                // Check for essential cross-compilation tools
                string clangPath = Path.Combine(path, "bin", "clang++.exe");
                string gccPath = Path.Combine(path, "bin", "x86_64-unknown-linux-gnu-gcc.exe");
                
                if (File.Exists(clangPath) || File.Exists(gccPath))
                {
                    foundToolchain = path;
                    break;
                }
            }
        }
        
        if (!string.IsNullOrEmpty(foundToolchain))
        {
            Console.WriteLine("OK (" + foundToolchain + ")");
            
            // Additional validation
            Console.Write("  Checking toolchain components... ");
            bool hasRequiredTools = true;
            
            string[] requiredFiles = {
                Path.Combine(foundToolchain, "bin", "clang++.exe"),
                Path.Combine(foundToolchain, "lib", "gcc"),
                Path.Combine(foundToolchain, "x86_64-unknown-linux-gnu")
            };
            
            foreach (string file in requiredFiles)
            {
                if (!File.Exists(file) && !Directory.Exists(file))
                {
                    hasRequiredTools = false;
                    break;
                }
            }
            
            if (hasRequiredTools)
            {
                Console.WriteLine("Complete");
                return true;
            }
            else
            {
                Console.WriteLine("Incomplete (missing components)");
                return false;
            }
        }
        else
        {
            Console.WriteLine("NOT FOUND!");
            Console.WriteLine();
            Console.WriteLine("⚠️  LINUX CROSS-COMPILATION SETUP REQUIRED:");
            Console.WriteLine("1. Download UE4 Linux Toolchain:");
            Console.WriteLine("   - For UE4.27: v19_clang-11.0.1-centos7");
            Console.WriteLine("   - Download from Epic Games or build from source");
            Console.WriteLine();
            Console.WriteLine("2. Extract toolchain to:");
            Console.WriteLine("   C:\\UnrealToolchains\\v19_clang-11.0.1-centos7\\");
            Console.WriteLine();
            Console.WriteLine("3. Alternative: Set LINUX_MULTIARCH_ROOT environment variable");
            Console.WriteLine("   pointing to your toolchain root directory");
            Console.WriteLine();
            Console.WriteLine("4. Official download locations:");
            Console.WriteLine("   - Epic Games Developer Portal");
            Console.WriteLine("   - GitHub releases for UE4 cross-compilation tools");
            Console.WriteLine();
            Console.WriteLine("5. Verify installation by checking for:");
            Console.WriteLine("   - bin/clang++.exe");
            Console.WriteLine("   - x86_64-unknown-linux-gnu/ directory");
            Console.WriteLine("   - lib/gcc/ directory");
            
            return false;
        }
    }

    static bool CheckJavaJDK()
    {
        Console.Write("Checking Java JDK... ");
        
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "javac",
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            var proc = Process.Start(psi);
            string output = proc.StandardOutput.ReadToEnd();
            string error = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            
            string versionOutput = !string.IsNullOrEmpty(output) ? output : error;
            
            if (versionOutput.ToLower().Contains("javac"))
            {
                // Extract version number
                if (versionOutput.Contains("1.8") || versionOutput.Contains("8."))
                {
                    Console.WriteLine("OK (" + versionOutput.Trim() + ")");
                    return true;
                }
                else
                {
                    Console.WriteLine("FOUND but may be incompatible (" + versionOutput.Trim() + ") - UE4.27.2 works best with JDK 8");
                    return true; // Still functional, just not optimal
                }
            }
            else
            {
                Console.WriteLine("NOT FOUND!");
                return false;
            }
        }
        catch
        {
            Console.WriteLine("NOT FOUND!");
            return false;
        }
    }

    static void CheckOculusIntegration()
    {
        Console.Write("Checking for Oculus Integration indicators... ");
        
        // Check for common Oculus SDK paths
        string[] oculusPaths = {
            "C:\\OculusSDK",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Oculus"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Oculus")
        };
        
        foreach (string path in oculusPaths)
        {
            if (Directory.Exists(path))
            {
                Console.WriteLine("OK (Found Oculus SDK at " + path + ")");
                return;
            }
        }
        
        // Check if Oculus app is installed
        string oculusApp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Oculus", "Support", "oculus-runtime", "OVRServer_x64.exe");
        if (File.Exists(oculusApp))
        {
            Console.WriteLine("OK (Oculus Runtime detected)");
            return;
        }
        
        Console.WriteLine("INFO - No Oculus SDK found, but may not be required for building");
    }

    static void GeneratePackagingCommand(string uprojectPath, string outputPath, string platform = "Android")
    {
        Console.WriteLine();
        Console.WriteLine($"=== PACKAGING COMMAND GENERATION ({platform.ToUpper()}) ===");
        
        // Validate platform
        if (platform.ToLower() != "android" && platform.ToLower() != "linux")
        {
            Console.WriteLine($"ERROR: Unsupported platform '{platform}'. Supported platforms: Android, Linux");
            return;
        }
        
        // Find UE4 Engine
        string enginePath = FindUnrealEngine();
        if (string.IsNullOrEmpty(enginePath))
        {
            Console.WriteLine("ERROR: Could not find Unreal Engine installation!");
            return;
        }
        
        Console.WriteLine($"Found UE4 Engine at: {enginePath}");
        
        // Platform-specific checks
        if (platform.ToLower() == "android")
        {
            // Check if Android support is installed
            if (!CheckUnrealAndroidSupport(enginePath))
            {
                Console.WriteLine();
                Console.WriteLine("ERROR: Android support not found in UE4 installation!");
                Console.WriteLine("Your UE4 source build is missing Android platform support.");
                GenerateAndroidBuildCommands(enginePath);
                return;
            }
        }
        else if (platform.ToLower() == "linux")
        {
            // Check if Linux cross-compilation toolchain is available
            if (!CheckLinuxCrossCompileToolchain())
            {
                Console.WriteLine();
                Console.WriteLine("ERROR: Linux cross-compilation toolchain not found!");
                Console.WriteLine("Install the Linux toolchain to enable Linux packaging.");
                return;
            }
        }
        
        string projectName = Path.GetFileNameWithoutExtension(uprojectPath);
        string absoluteUprojectPath = Path.GetFullPath(uprojectPath);
        string absoluteOutputPath = Path.GetFullPath(outputPath);
        
        // UE4 Automation Tool path
        string automationTool = Path.Combine(enginePath, "Engine", "Binaries", "DotNET", "AutomationTool.exe");
        
        if (!File.Exists(automationTool))
        {
            Console.WriteLine($"ERROR: AutomationTool.exe not found at: {automationTool}");
            return;
        }
        
        Console.WriteLine();
        Console.WriteLine("=== GENERATED PACKAGING COMMAND ===");
        Console.WriteLine();
        
        string command;
        string batchFilename;
        string expectedOutput;
        
        if (platform.ToLower() == "android")
        {
            // Generate Android packaging command
            command = $"\"{automationTool}\" BuildCookRun " +
                $"-project=\"{absoluteUprojectPath}\" " +
                $"-platform=Android " +
                $"-targetplatform=Android " +
                $"-clientconfig=Shipping " +
                $"-cook " +
                $"-compressed " +
                $"-iterativecooking " +
                $"-allmaps " +
                $"-build " +
                $"-stage " +
                $"-pak " +
                $"-package " +
                $"-archive " +
                $"-archivedirectory=\"{absoluteOutputPath}\" " +
                $"-prereqs " +
                $"-nodebuginfo " +
                $"-nocompileeditor " +
                $"-NoSubmit " +
                $"-utf8output";
                
            batchFilename = $"Package_{projectName}_Android.bat";
            expectedOutput = $"{absoluteOutputPath}\\Android_ASTC\\{projectName}\\Binaries\\Android\\{projectName}-Android-Shipping.apk";
        }
        else // Linux
        {
            // Generate Linux packaging command
            command = $"\"{automationTool}\" BuildCookRun " +
                $"-project=\"{absoluteUprojectPath}\" " +
                $"-platform=Linux " +
                $"-targetplatform=Linux " +
                $"-clientconfig=Shipping " +
                $"-cook " +
                $"-compressed " +
                $"-iterativecooking " +
                $"-allmaps " +
                $"-build " +
                $"-stage " +
                $"-pak " +
                $"-package " +
                $"-archive " +
                $"-archivedirectory=\"{absoluteOutputPath}\" " +
                $"-nodebuginfo " +
                $"-nocompileeditor " +
                $"-NoSubmit " +
                $"-utf8output";
                
            batchFilename = $"Package_{projectName}_Linux.bat";
            expectedOutput = $"{absoluteOutputPath}\\Linux\\{projectName}\\Binaries\\Linux\\{projectName}";
        }
            
        Console.WriteLine(command);
        Console.WriteLine();
        
        // Also generate a batch file for convenience
        string batchFilePath = Path.Combine(Path.GetDirectoryName(absoluteUprojectPath), batchFilename);
        
        try
        {
            // Delete existing batch file to ensure fresh generation
            if (File.Exists(batchFilePath))
            {
                File.Delete(batchFilePath);
                Console.WriteLine($"Deleted existing batch file: {batchFilePath}");
            }
            
            string buildType = platform.ToUpper() == "ANDROID" ? "SHIPPING BUILD" : "SHIPPING BUILD";
            string batchContent = "@echo off\n" +
                "echo Packaging " + projectName + " for " + platform + " (" + buildType + ")...\n" +
                "echo.\n" +
                command + "\n" +
                "echo.\n" +
                "if %ERRORLEVEL% EQU 0 (\n" +
                "    echo Packaging completed successfully!\n" +
                ") else (\n" +
                "    echo ERROR: Packaging failed with exit code %ERRORLEVEL%\n" +
                "    echo Check the log files for more details.\n" +
                ")\n" +
                "pause\n";
                
            File.WriteAllText(batchFilePath, batchContent);
            Console.WriteLine($"NEW {platform} Shipping batch file created: {batchFilePath}");
            Console.WriteLine($"You can run this batch file to start the {platform.ToUpper()} packaging process.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not create batch file: {ex.Message}");
        }
        
        Console.WriteLine();
        Console.WriteLine("=== EXPECTED OUTPUT LOCATION ===");
        Console.WriteLine($"The final package should be located at:");
        Console.WriteLine(expectedOutput);
        
        if (platform.ToLower() == "android")
        {
            Console.WriteLine();
            Console.WriteLine("If the APK ends up in a simple 'Android' folder instead of 'Android_ASTC',");
            Console.WriteLine("it indicates the packaging didn't complete properly or used wrong parameters.");
        }
        
        Console.WriteLine();
        Console.WriteLine("=== PACKAGING NOTES ===");
        
        if (platform.ToLower() == "android")
        {
            Console.WriteLine("- This command generates a SHIPPING BUILD for development/testing");
            Console.WriteLine("- The APK will be ~1.5GB and include all content, assets, and prerequisites");
            Console.WriteLine("- Uses debug keystore (no distribution signing required)");
            Console.WriteLine("- Install/Uninstall batch files will be created in the output directory");
            Console.WriteLine("- Ensure your project is set up for Android development");
            Console.WriteLine("- Make sure Android platform is installed in your UE4 project");
            Console.WriteLine("- The packaging process may take 45-90 minutes depending on project size");
            Console.WriteLine("- Check the output directory for:");
            Console.WriteLine("  * .apk file (the Android application)");
            Console.WriteLine("  * Install_[ProjectName].bat (installation script)");
            Console.WriteLine("  * Uninstall_[ProjectName].bat (uninstall script)");
            Console.WriteLine("- If errors occur, check the AutomationTool log files for detailed information");
            Console.WriteLine();
            Console.WriteLine("NOTE: For Google Play Store distribution, you'll need to:");
            Console.WriteLine("- Create a distribution keystore in Project Settings > Android");
            Console.WriteLine("- Add back the -distribution flag to the packaging command");
        }
        else // Linux
        {
            Console.WriteLine("- This command generates a SHIPPING BUILD for Linux deployment");
            Console.WriteLine("- The Linux executable will include all content, assets, and dependencies");
            Console.WriteLine("- Cross-compiled from Windows using the Linux toolchain");
            Console.WriteLine("- Ensure your project is set up for Linux development");
            Console.WriteLine("- Make sure Linux platform is installed in your UE4 project");
            Console.WriteLine("- The packaging process may take 30-60 minutes depending on project size");
            Console.WriteLine("- Check the output directory for:");
            Console.WriteLine("  * Linux executable (the main application)");
            Console.WriteLine("  * .pak files (packaged content)");
            Console.WriteLine("  * Linux shared libraries (.so files)");
            Console.WriteLine("- If errors occur, check the AutomationTool log files for detailed information");
            Console.WriteLine();
            Console.WriteLine("DEPLOYMENT NOTES:");
            Console.WriteLine("- Transfer the entire Linux folder to your Linux target system");
            Console.WriteLine("- Make the executable file executable: chmod +x " + projectName);
            Console.WriteLine("- Run from terminal: ./" + projectName);
            Console.WriteLine("- Ensure target Linux system has required libraries (OpenGL, etc.)");
        }
    }
    
    static bool CheckUnrealAndroidSupport(string enginePath)
    {
        Console.WriteLine("Checking UE4 Android platform support...");
        
        bool hasAndroidSupport = true;
        
        // Check for Android automation scripts
        string androidAutomationDll = Path.Combine(enginePath, "Engine", "Binaries", "DotNET", "AutomationScripts", "Android", "Android.Automation.dll");
        if (!File.Exists(androidAutomationDll))
        {
            Console.WriteLine($"❌ Missing: {androidAutomationDll}");
            hasAndroidSupport = false;
        }
        else
        {
            Console.WriteLine($"✅ Found: Android.Automation.dll");
        }
        
        // Check for Android platform files in source
        string androidPlatformPath = Path.Combine(enginePath, "Engine", "Source", "Programs", "UnrealBuildTool", "Platform", "Android");
        if (!Directory.Exists(androidPlatformPath))
        {
            Console.WriteLine($"❌ Missing: {androidPlatformPath}");
            hasAndroidSupport = false;
        }
        else
        {
            Console.WriteLine($"✅ Found: UnrealBuildTool Android platform source");
        }
        
        // Check for Android target platform module
        string androidTargetPlatform = Path.Combine(enginePath, "Engine", "Source", "Developer", "Android");
        if (!Directory.Exists(androidTargetPlatform))
        {
            Console.WriteLine($"❌ Missing: {androidTargetPlatform}");
            hasAndroidSupport = false;
        }
        else
        {
            Console.WriteLine($"✅ Found: Android target platform module");
        }
        
        // Check for additional Android-related binaries
        string androidRuntimePath = Path.Combine(enginePath, "Engine", "Source", "Runtime", "Launch", "Private", "Android");
        if (Directory.Exists(androidRuntimePath))
        {
            Console.WriteLine($"✅ Found: Android runtime support");
        }
        else
        {
            Console.WriteLine($"❌ Missing: Android runtime support");
        }
        
        // Check UBT for Android toolchain
        string androidToolchainPath = Path.Combine(enginePath, "Engine", "Source", "Programs", "UnrealBuildTool", "Platform", "Android", "AndroidToolChain.cs");
        if (File.Exists(androidToolchainPath))
        {
            Console.WriteLine($"✅ Found: Android toolchain source");
        }
        else
        {
            Console.WriteLine($"❌ Missing: Android toolchain source");
        }
        
        if (!hasAndroidSupport)
        {
            Console.WriteLine();
            Console.WriteLine("🔧 TO FIX ANDROID SUPPORT IN YOUR UE4 SOURCE BUILD:");
            Console.WriteLine("1. Ensure Android SDK/NDK environment variables are set:");
            Console.WriteLine("   - ANDROID_HOME or ANDROID_SDK_ROOT");
            Console.WriteLine("   - ANDROID_NDK_HOME (optional but recommended)");
            Console.WriteLine();
            Console.WriteLine("2. From your UE4 source directory, run:");
            Console.WriteLine("   .\\Setup.bat");
            Console.WriteLine("   .\\GenerateProjectFiles.bat");
            Console.WriteLine();
            Console.WriteLine("3. Build UE4 Editor (includes automation tools):");
            Console.WriteLine("   .\\Engine\\Build\\BatchFiles\\Build.bat UE4Editor Win64 Development");
            Console.WriteLine();
            Console.WriteLine("4. Alternative: Use Visual Studio:");
            Console.WriteLine("   - Open UE4.sln in Visual Studio");
            Console.WriteLine("   - Build Solution (Development Editor | Win64)");
            Console.WriteLine();
            
            // List available build targets for debugging
            ListAvailableBuildTargets(enginePath);
        }
        else
        {
            Console.WriteLine("✅ Android platform support verified in UE4 installation");
        }
        
        return hasAndroidSupport;
    }
    
    static void GenerateAndroidBuildCommands(string enginePath)
    {
        Console.WriteLine();
        Console.WriteLine("=== ANDROID SUPPORT REBUILD COMMANDS ===");
        Console.WriteLine();
        Console.WriteLine("Copy and run these commands in your UE4 source directory:");
        Console.WriteLine($"cd /d \"{enginePath}\"");
        Console.WriteLine();
        Console.WriteLine("REM CRITICAL: Set Android environment variables BEFORE building:");
        Console.WriteLine("set ANDROID_HOME=P:\\dev\\airbound_4.27\\UE_4.27_sdks\\android-sdk");
        Console.WriteLine("set ANDROID_NDK_HOME=P:\\dev\\airbound_4.27\\UE_4.27_sdks\\android-sdk\\ndk\\21.1.6352462");
        Console.WriteLine();
        Console.WriteLine("REM 1. Clean existing automation tools (important!):");
        Console.WriteLine("if exist Engine\\Binaries\\DotNET\\AutomationTool rmdir /s /q Engine\\Binaries\\DotNET\\AutomationTool");
        Console.WriteLine("if exist Engine\\Binaries\\DotNET\\AutomationScripts rmdir /s /q Engine\\Binaries\\DotNET\\AutomationScripts");
        Console.WriteLine();
        Console.WriteLine("REM 2. Update dependencies and generate project files:");
        Console.WriteLine("Setup.bat");
        Console.WriteLine("GenerateProjectFiles.bat");
        Console.WriteLine();
        Console.WriteLine("REM 3. Build UE4Editor (this rebuilds automation tools with Android support):");
        Console.WriteLine("Engine\\Build\\BatchFiles\\Build.bat UE4Editor Win64 Development");
        Console.WriteLine();
        Console.WriteLine("REM 4. Verify Android.Automation.dll was created:");
        Console.WriteLine("dir Engine\\Binaries\\DotNET\\AutomationScripts\\Android\\Android.Automation.dll");
        Console.WriteLine();
        Console.WriteLine("REM 5. Alternative: Build automation tools manually with MSBuild:");
        Console.WriteLine("msbuild Engine\\Source\\Programs\\AutomationTool\\AutomationTool.csproj -p:Configuration=Development");
        Console.WriteLine("msbuild Engine\\Source\\Programs\\AutomationTool\\Scripts\\AutomationScripts.Automation.csproj -p:Configuration=Development");
        Console.WriteLine();
        Console.WriteLine("REM 6. If still missing, rebuild from Visual Studio:");
        Console.WriteLine("REM    - Set environment variables in a NEW Command Prompt");
        Console.WriteLine("REM    - Open UE4.sln in Visual Studio from that Command Prompt");
        Console.WriteLine("REM    - Build Solution (Development Editor | Win64)");
        Console.WriteLine();
        Console.WriteLine("IMPORTANT: Android SDK/NDK must be detected during build for Android.Automation.dll");
        Console.WriteLine("           to be created. Check build logs for Android SDK detection messages.");
    }
    
    static void ListAvailableBuildTargets(string enginePath)
    {
        Console.WriteLine();
        Console.WriteLine("=== AVAILABLE BUILD TARGETS ===");
        
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(enginePath, "Engine", "Build", "BatchFiles", "Build.bat"),
                Arguments = "-list",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = enginePath
            };
            
            var proc = Process.Start(psi);
            string output = proc.StandardOutput.ReadToEnd();
            string error = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            
            if (!string.IsNullOrEmpty(output))
            {
                Console.WriteLine("Available build targets:");
                Console.WriteLine(output);
            }
            
            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine("Build system output:");
                Console.WriteLine(error);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not list build targets: {ex.Message}");
        }
    }
    
    static string FindUnrealEngine()
    {
        // Common UE4 installation paths
        string[] possiblePaths = {
            // Epic Games Launcher installations
            "C:\\Program Files\\Epic Games\\UE_4.27",
            "C:\\Program Files (x86)\\Epic Games\\UE_4.27",
            
            // Custom source builds (common locations)
            "P:\\prj\\UE427_src\\UnrealEngine",  // User's specific source build
            "C:\\UnrealEngine",
            "C:\\UE4",
            "C:\\UE_4.27",
            "D:\\UnrealEngine",
            "D:\\UE4",
            "D:\\UE_4.27",
            
            // Check if there's an environment variable
            Environment.GetEnvironmentVariable("UE4_ROOT"),
            Environment.GetEnvironmentVariable("UNREAL_ENGINE_ROOT")
        };
        
        foreach (string path in possiblePaths)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                // Verify it's a valid UE4 installation
                string automationTool = Path.Combine(path, "Engine", "Binaries", "DotNET", "AutomationTool.exe");
                if (File.Exists(automationTool))
                {
                    return path;
                }
            }
        }
        
        // Try to find UE4 through registry (Epic Games Launcher installations)
        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\EpicGames\Unreal Engine"))
            {
                if (key != null)
                {
                    foreach (string subKeyName in key.GetSubKeyNames())
                    {
                        using (var subKey = key.OpenSubKey(subKeyName))
                        {
                            var installLocation = subKey?.GetValue("InstalledDirectory") as string;
                            if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                            {
                                string automationTool = Path.Combine(installLocation, "Engine", "Binaries", "DotNET", "AutomationTool.exe");
                                if (File.Exists(automationTool))
                                {
                                    return installLocation;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Registry access failed, continue with other methods
        }
        
        return null;
    }
}