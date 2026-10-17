using System.Text.RegularExpressions;

/* Sources Used
 * MAC Address Generation - https://stackoverflow.com/questions/16448519/c-sharp-regex-validating-mac-address
*/

namespace SmartX.Shared.Models
{
    /// <summary>
    /// One definition of what a device key looks like, used by every dictionary keyed on it.
    ///
    /// A MAC address can legally arrive as 24:6f:28:aa:bb:cc, 24-6F-28-AA-BB-CC or any mix of the
    /// two. Hashing those raw strings would give one physical device several dictionary entries,
    /// so a telemetry packet could miss its own registration. Normalize collapses every spelling to
    /// one canonical upper-case, colon-separated key, and Comparer makes the lookups case-insensitive
    /// for node identifiers (ESP32-ENV-01 vs esp32-env-01) too.
    /// </summary>
    public static partial class DeviceIdentifier
    {
        [GeneratedRegex("^([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}$")]
        private static partial Regex MacAddressPattern();

        [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{2,31}$")]
        private static partial Regex NodeIdentifierPattern();


        /// <summary>
        /// Comparer every device-keyed dictionary and set should be built with.
        /// </summary>
        public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;


        /// <summary>
        /// True for a MAC address in either separator style.
        /// </summary>
        public static bool IsMacAddress(string? value) =>
            !string.IsNullOrWhiteSpace(value) && MacAddressPattern().IsMatch(value.Trim());


        /// <summary>
        /// True for a MAC address or a node identifier such as ESP32-ENV-04.
        /// </summary>
        public static bool IsValid(string? value) =>
            !string.IsNullOrWhiteSpace(value)
            && (MacAddressPattern().IsMatch(value.Trim()) || NodeIdentifierPattern().IsMatch(value.Trim()));
        //----------------------------------------------------------//


        /// <summary>
        /// Canonical dictionary key for a device. MAC addresses become upper-case and colon separated;
        /// node identifiers are trimmed and otherwise left as the operator typed them, since the
        /// case-insensitive comparer already makes their lookups safe.
        /// </summary>
        public static string Normalize(string value)
        {
            string trimmed = value.Trim();

            return MacAddressPattern().IsMatch(trimmed)
                ? trimmed.Replace('-', ':').ToUpperInvariant()
                : trimmed;
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
