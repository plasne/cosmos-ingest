using System.Collections.Generic;

namespace DataModel
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

    public class MyDoc : CosmosItem
    {

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

    }

}