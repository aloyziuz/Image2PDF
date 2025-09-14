using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Images2PDF
{
    public class WindowsFileNameComparer : IComparer<string>
    {
        public int Compare(string x, string y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            var xName = Path.GetFileName(x);
            var yName = Path.GetFileName(y);

            // Extract base part and parenthetical number
            var regex = new Regex(@"^(?<base>\D+)?[-|_|\s]*(?<baseNum>\d+)?[-|_|\s]*(?<subAlpha>\D+)?[-|_|\s]*(?<subNum>\d+)?(?:\s*\((?<paren>\d+)\))?(?<extension>\.\w+)*$");

            var matchX = regex.Match(xName);
            var matchY = regex.Match(yName);

            var baseX = matchX.Groups["base"].Value;
            var baseY = matchY.Groups["base"].Value;

            var numX = matchX.Groups["baseNum"].Success ? long.Parse(matchX.Groups["baseNum"].Value) : 0;
            var numY = matchY.Groups["baseNum"].Success ? long.Parse(matchY.Groups["baseNum"].Value) : 0;

            var subbaseX = matchX.Groups["subAlpha"].Value;
            var subbaseY = matchY.Groups["subAlpha"].Value;

            var subnumX = matchX.Groups["subNum"].Success ? long.Parse(matchX.Groups["subNum"].Value) : 0;
            var subnumY = matchY.Groups["subNum"].Success ? long.Parse(matchY.Groups["subNum"].Value) : 0;

            var numBracketX = matchX.Groups["paren"].Success ? long.Parse(matchX.Groups["paren"].Value) : 0;
            var numBracketY = matchY.Groups["paren"].Success ? long.Parse(matchY.Groups["paren"].Value) : 0;

            // Compare base strings using natural sorting
            int compare = StringCompare(baseX, baseY);
            if (compare != 0) return compare;

            //if base strings are equal, compare the trailing numbers
            compare = numX.CompareTo(numY);
            if (compare != 0) return compare;

            // Compare base strings using natural sorting
            compare = StringCompare(subbaseX, subbaseY);
            if (compare != 0) return compare;

            //if base strings are equal, compare the trailing numbers
            compare = subnumX.CompareTo(subnumY);
            if (compare != 0) return compare;

            // If base strings and numbers are equal, compare the numbers in parentheses
            return numBracketX.CompareTo(numBracketY);
        }

        private int StringCompare(string a, string b)
        {
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
