using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VTMF.SQLInstaller.Entities;

namespace VTMF.SQLInstaller.Installers;
public class EncryptUtils
{
    public static string GenerateRandomPassword(int length = 32)
    {
        const string validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890!@#%^&*()-_=+[]{}|;:,.<>?";
        var random = new Random();
        return new string(Enumerable.Repeat(validChars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    public static string RunShellEncryptorJar(string idcode, string username, string password)
    {

        var scriptPath = "/scripts/encryptpassword.sh";
        //encryptpassword.sh --idcode <IDCODE> --password <PASSWORD> --username <USERNAME> [--silent]

        var processInfo = new System.Diagnostics.ProcessStartInfo(scriptPath, $"--idcode {idcode} --password '{password}' --username {username} --silent")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = System.Diagnostics.Process.Start(processInfo);
        if (process == null)
        {
            throw new Exception($"Failed to start {scriptPath}");
        }
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new Exception($"Error running {scriptPath}: {error}");
        }
        return process.StandardOutput.ReadToEnd().Trim();
    }

    public static string ReadEncryptedFile(string path)
    {
        var fileBytes = System.IO.File.ReadAllBytes(path);
        var encoded = Convert.ToBase64String(fileBytes);
        return encoded;
    }

    public static List<AdminUser> ParseAdmins(string adminString)
    {
        var adminUsers = new List<AdminUser>();
        var admins = adminString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var admin in admins)
        {
            var parts = admin.Split('-', 2);
            var idcode = parts.Length > 0 ? parts[0] : null;
            var username = parts.Length > 1 ? parts[1] : parts[0];
            if (idcode != null && username != null)
            {
                adminUsers.Add(new AdminUser(idcode, username));
            }
        }
        return adminUsers;
    }
}
