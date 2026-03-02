using System;
using System.Reflection;
using Amazon.Runtime.Documents;
using System.IO;

namespace AgentControlPanel.Services;

public class DocumentInspector
{
    public static void Inspect()
    {
        var docType = typeof(Document);
        using (var writer = new StreamWriter("document_members.txt"))
        {
            writer.WriteLine($"Methods of {docType.FullName}:");
            foreach (var method in docType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                writer.WriteLine($" - {method.Name}");
            }
            writer.WriteLine("\nProperties:");
            foreach (var prop in docType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                writer.WriteLine($" - {prop.Name}");
            }
        }
    }
}
