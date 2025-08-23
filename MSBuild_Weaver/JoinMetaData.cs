using Microsoft.Build.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Weaver
{
    public partial class JoinMetaData : Microsoft.Build.Utilities.Task
    {
        [Required] public string AssemblyName { get; set; }
        [Required] public string OutputPath { get; set; } 
        [Required] public string WeaveRoot { get; set; }  
        public override bool Execute()
        {


            string MetaDataDir = Path.Combine(WeaveRoot, "MetaData");
            string outputFile = Path.Combine(OutputPath, AssemblyName + ".MetaData.json");
            if (File.Exists(outputFile))
                File.Delete(outputFile);
            using (StreamWriter sw = File.AppendText(outputFile))
            {
                sw.WriteLine("{");
                AppendAllFileToFile(MetaDataDir, "ClassMetaData", sw, true);
                AppendAllFileToFile(MetaDataDir, "StructMetaData", sw);
                AppendAllFileToFile(MetaDataDir, "EnumMetaData", sw);
                AppendAllFileToFile(MetaDataDir, "InterfaceMetaData", sw);
                AppendAllFileToFile(MetaDataDir, "DelegateMetaData", sw);
                sw.WriteLine("}");
            }


            // Get all file in path
            return true;
   

        }

        // Read All file from a directory and append them to another file
        public void AppendAllFileToFile(string path,string type, StreamWriter sw, bool IsFirst = false) 
        {
            path = Path.Combine(path, type);

            if (!Directory.Exists(path))
            {
                sw.Write((IsFirst ?"" : ","+ Environment.NewLine)+ "\"" + type + "\": []");
                return;
            }
                

            bool firstFile = true;
            if (!IsFirst)
            {
                sw.Write("," + Environment.NewLine);
            }
            sw.Write("" + "\"" + type + "\": [");
            foreach (var file in Directory.GetFiles(path))
            {
                
                using (StreamReader sr = new StreamReader(file))
                {
                    if (!firstFile)
                    {
                        sw.Write("," + Environment.NewLine);
                    }
                    sw.Write(sr.ReadToEnd());
                    firstFile = false;

                }
            }
            sw.Write("]");

        }
    }
}
