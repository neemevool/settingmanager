using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VTMF.SQLInstaller.Installers;
internal static class DatabaseUtils
{
    /// <summary>
    /// Escapes single quotes in SQL strings to prevent SQL injection
    /// </summary>
    internal static string EscapeSqlString(string value)
    {
        // remove all characters except letters, digits, underscore, and dollar sign
        return Regex.Replace(value, @"[^\w]", "");

    }
}
