using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Test_App_Sente
{
    // Klasa reprezentująca metadane pojedynczej kolumny
    public class ColumnMetadata
    {
        public string TableName { get; set; }
        public string ColumnName { get; set; }
        public int Position { get; set; }
        public bool IsNotNull { get; set; }
        public string DefaultValueSource { get; set; }
        public int FieldLength { get; set; }
        public int FieldScale { get; set; }
        public int FieldTypeId { get; set; }
        public int FieldSubType { get; set; }
        public int CharLength { get; set; }
        public string DomainName { get; set; }
    }

}
