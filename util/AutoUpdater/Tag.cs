using System;
using System.Collections.Generic;

namespace AutoUpdater
{
    internal class Tag
    {
        private string tag;
        private List<int> versionNumbers;

        public Tag(string str_tag)
        {
            tag = str_tag;
            versionNumbers = new List<int>();

            extractVersionNumbers();
        }

        private void extractVersionNumbers()
        {
            // tag is expected to be in the format Vx.y.z (where x, y and z are integers - there can be more numbers)
            if (isTagFormatCorrect(tag))
            {
                // Remove the first car (v/V), then split the tag into substrings containing each number
                string[] str_array = tag.Substring(1).Split('.');

                // Read normally the numbers
                for (int i = 0; i < str_array.Length; i++)
                {
                    versionNumbers.Add(int.Parse(str_array[i], System.Globalization.NumberStyles.None));
                }
            }
            else
            {
                throw new Exception("Invalid tag format : " + tag);
            }
        }

        private bool isTagFormatCorrect(string str_tag)
        {
            // check that the tag has at least the minimum possible length
            if (str_tag.Length < 2) return false;

            // check that the tag starts with 'v' or 'V'
            if (!isVersionCar(str_tag[0])) return false;

            // check that the rest of the tag is only compound of digits and dots,
            // while not having two dots following eachother, or 'V' followed by a dot
            bool isDigitExpected = true;

            for (int i = 1; i < str_tag.Length; i++)
            {
                if (isDot(str_tag[i]))
                {
                    if (isDigitExpected) return false;
                    else isDigitExpected = true;
                }
                else if (isDigit(str_tag[i]))
                {
                    isDigitExpected = false;
                }
                else
                {
                    return false;
                }
            }

            // check that the tag ends with a digit
            if (isDigitExpected) { return false; }

            // If we reached that point, all good!
            return true;
        }

        private bool isVersionCar(char c)
        {
            return (c == 'v' || c == 'V');
        }

        private bool isDigit(char c)
        {
            return (c >= '0' && c <= '9');
        }

        private bool isDot(char c)
        {
            return (c == '.');
        }

        public static bool operator <(Tag tag1, Tag tag2)
        {
            // tags may have different size (like if we compare V1.3 and V1.3.1), so we take that into account
            int minSize = Math.Min(tag1.versionNumbers.Count, tag2.versionNumbers.Count);

            // first, compare numbers one to one...
            for (int i = 0; i < minSize; i++)
            {
                if (tag1.versionNumbers[i] != tag2.versionNumbers[i])
                {
                    return (tag1.versionNumbers[i] < tag2.versionNumbers[i]);
                }
            }

            // if some numbers remain for only one tag, this one is the greatest
            return (tag1.versionNumbers.Count < tag2.versionNumbers.Count);
        }

        public static bool operator >(Tag tag1, Tag tag2)
        {
            return (tag2 < tag1);
        }

        public override string ToString()
        {
            return tag;
        }
    }
}
