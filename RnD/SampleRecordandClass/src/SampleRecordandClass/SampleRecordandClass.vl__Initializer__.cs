extern alias e1;
extern alias e2;
extern alias e211;
using n4 = e1::VL.Core.CompilerServices;
using n1 = e2::VL.Core.CompilerServices;
using n2 = e2::VL.Core;
using n5 = e1::VL.Core;
using n8 = e2::VL.AppServices.CompilerServices;
using n6 = e2::VL.Model;
using n3 = global::_SampleRecordandClass_;
using n9 = e211::_VL_CoreLib_;
using n7 = global::_SampleRecordandClass_.Main;
[assembly: n1.CompilerVersion(@"2025.7.1")]
[assembly: n1.SymbolSourceReference(@"VL.CoreLib.vl", n2.SymbolSourceKind.Document, false, false)]
[assembly: n1.SymbolSourceReference(@"SampleProj.csproj", n2.SymbolSourceKind.Project, false, false)]
[assembly: n4.AssemblyInitializer(typeof(n3.EKYvICxF27COKXc5f2ctJUInitializer))]
[assembly: n1.TypeImport(@"EKYvICxF27COKXc5f2ctJU", @"SEeVW6E6jOkLZwCgUlRJjT", @"MyRecord", @"Main", n5.SymbolSmell.Default, n6.KnownTypeStructure.None, typeof(n7.MyRecord_R), null, n8.Mutability.Immutable, true)]
[assembly: n1.TypeImport(@"EKYvICxF27COKXc5f2ctJU", @"CCsK7ereJ3QNbutsX95rnL", @"MyTestClass", @"Main", n5.SymbolSmell.Default, n6.KnownTypeStructure.None, typeof(n7.MyTestClass_C), null, n8.Mutability.Mutable, true)]
[assembly: n1.TypeImport(@"EKYvICxF27COKXc5f2ctJU", @"Ks3DYhvYG4SMQdYCFwtLnR", @"MyCustomSlot", @"Main", n5.SymbolSmell.Default, n6.KnownTypeStructure.None, typeof(n7.MyCustomSlot_I), null, n8.Mutability.Auto, true)]
[assembly: n1.TypeImport(@"EKYvICxF27COKXc5f2ctJU", @"SqlmNF6xKBuLjplpZfhGnP", @"SampleRecordandClassApplication", @"Main", n5.SymbolSmell.Hidden, n6.KnownTypeStructure.None, typeof(n7.SampleRecordandClassApplication_P), null, n8.Mutability.Auto, true)]
[assembly: n1.Process(@"Application", @"", n5.SymbolSmell.Hidden, typeof(n7.SampleRecordandClassApplication_P), false, [@"Create", @"Update"], [@"Update"])]
[assembly: n8.AdaptiveImplementations(typeof(n3.__AdaptiveImplementations__EKYvICxF27COKXc5f2ctJU))]
[assembly: n8.AdaptiveNodeLookup(typeof(n3.__AdaptiveNodeLookup__EKYvICxF27COKXc5f2ctJU))]
namespace _SampleRecordandClass_
{
    public sealed class EKYvICxF27COKXc5f2ctJUInitializer : n1.PatchedAssemblyInitializer<n3.EKYvICxF27COKXc5f2ctJUInitializer>
    {
        public override sealed void CollectDependencies(n4.DependencyCollector collector){
            collector.AddDependency(n9.LMFQrbYrtQvO4pn4vSywS3Initializer.Default);
            base.CollectDependencies(collector);
        }
        public override sealed void Configure(n5.AppHost appHost){
            base.Configure(appHost);
        }
    }
}
