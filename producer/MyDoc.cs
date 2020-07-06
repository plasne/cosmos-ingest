using System;
using System.Collections.Generic;

namespace producer
{

    public class Option
    {

        public string optAttribA { get; set; }
        public string optAttribB { get; set; }
        public string optAttribC { get; set; }
        public string optAttribD { get; set; }
        public string optAttribE { get; set; }
        public string optAttribF { get; set; }
        public string optAttribG { get; set; }
        public string optAttribH { get; set; }

    }

    public class MyDoc
    {

        private static readonly System.Random random = new System.Random();

        public string id { get; set; }
        public string key { get; set; }
        public string date { get; set; }
        public string locId { get; set; }
        public string attribA { get; set; }
        public string attribB { get; set; }
        public string attribC { get; set; }
        public string attribD { get; set; }
        public string attribE { get; set; }
        public bool isDeleted { get; set; }
        public string attribF { get; set; }
        public string attribG { get; set; }
        public List<Option> options { get; set; }

        public static MyDoc Generate()
        {

            // randomize the data
            var locId = random.Next(1, 700);
            var partition = random.Next(0, 100);
            var year = random.Next(2000, 2020);
            var month = random.Next(1, 12);
            var day = random.Next(1, 28);
            var options = random.Next(1, 3);

            // generate the document
            var myDoc = new MyDoc()
            {
                id = System.Guid.NewGuid().ToString(),
                key = $"{locId.ToString()}-{partition.ToString().PadLeft(3, '0')}",
                date = $"{year.ToString()}-{month.ToString().PadLeft(2, '0')}-{day.ToString().PadLeft(2, '0')}",
                locId = locId.ToString(),
                attribA = "",
                attribB = "MIGRATOR",
                attribC = DateTime.UtcNow.ToString("o"),
                attribD = "",
                attribE = "",
                isDeleted = false,
                attribF = "",
                attribG = "",
                options = new List<Option>()
            };

            // generate waves
            for (int i = 0; i < options; i++)
            {
                myDoc.options.Add(new Option()
                {
                    optAttribA = random.Next(-10, 10).ToString(),
                    optAttribB = random.Next(1, 10).ToString(),
                    optAttribC = "0",
                    optAttribD = "0",
                    optAttribE = $"OPTION {i.ToString()}",
                    optAttribF = i.ToString(),
                    optAttribG = "",
                    optAttribH = "0"
                });
            }

            return myDoc;
        }

    }

}