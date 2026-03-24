extern alias e1;
extern alias e2;
extern alias e211;
extern alias e337;
using n13 = global::System.Collections.Generic;
using n9 = e1::VL.Core.CompilerServices;
using n14 = e337::Main;
using n3 = e2::VL.Core.CompilerServices;
using n12 = e1::VL.Core.EditorAttributes;
using n10 = e2::VL.AppServices;
using n5 = e2::VL.Core;
using n8 = e1::VL.Core;
using n15 = e211::_CoreLibBasics_.System;
using n1 = e2::VL.AppServices.CompilerServices;
using n7 = e1::VL.Model;
using n2 = global::System;
using n6 = e1::VL.Core.Import;
using n11 = global::System.Runtime.CompilerServices;
using n16 = e211::_CoreLibBasics_.Primitive;
using n4 = global::_SampleRecordandClass_.Main;
namespace _SampleRecordandClass_.Main
{
    [n1.Element(DocumentId = @"EKYvICxF27COKXc5f2ctJU", PersistentId = @"SEeVW6E6jOkLZwCgUlRJjT")]
    [n2.Serializable]
    [n3.Name(@"MyRecord")]
    public sealed class MyRecord_R : n5.PatchedObject<n4.MyRecord_R>, n2.IDisposable
    {
        [return: n6.Pin(IsState = true)]
        [n9.CreateNew]
        [n1.Element(DocumentId = @"EKYvICxF27COKXc5f2ctJU", PersistentId = @"LUHvEjgU8PyLTvwKy0HnO9")]
        [n3.ShowCategory(true)]
        public static n4.MyRecord_R Create([n6.Pin(Name = @"Node Context", Visibility = n7.PinVisibility.Hidden)] n8.NodeContext Node_Context, [n6.Pin(Name = @"MyFloat")] float MyFloat_In){
            Node_Context = Node_Context.WithDefinitionId(@"EKYvICxF27COKXc5f2ctJU", @"SEeVW6E6jOkLZwCgUlRJjT").WithIsImmutable(true);
            var instance = new MyRecord_R(Node_Context, n5.PatchedObject.NewIdentity());
            return instance.__Create__(Node_Context, MyFloat_In);
        }
        [n2.ThreadStatic]
        private static n4.MyRecord_R __instanceBeingConstructed__;
        private static n4.MyRecord_R __DEFAULT__;
        [return: n6.Pin(IsState = true)]
        [n9.CreateDefault]
        [n1.Element()]
        [n3.ShowCategory(true)]
        [n6.Smell(n8.SymbolSmell.Hidden)]
        public static n4.MyRecord_R CreateDefault(){
            return __DEFAULT__ ??= __COMPUTE__();
            
            n4.MyRecord_R __COMPUTE__()
            {
                if (__instanceBeingConstructed__ != null)
                {
                    return n10.CompilationHelper.ReportRecursive(__instanceBeingConstructed__);
                }
                try
                {
                    var context = n8.NodeContext.CurrentRoot.WithDefinitionId(@"EKYvICxF27COKXc5f2ctJU", @"SEeVW6E6jOkLZwCgUlRJjT").WithIsImmutable(true);
                    var instance = new MyRecord_R(context, n5.PatchedObject.NewIdentity());
                    __instanceBeingConstructed__ = instance;
                    return instance.__CreateDefault__();
                }
                finally
                {
                    __instanceBeingConstructed__ = null;
                }
            }
        }
        [return: n6.Pin(Visibility = n7.PinVisibility.Optional, IsState = true)]
        [n1.Element(DocumentId = @"EKYvICxF27COKXc5f2ctJU", PersistentId = @"EzJzA956LWuL6pjDBoeK47")]
        [n3.ShowCategory(true)]
        public n4.MyRecord_R Split([n6.Pin(Name = @"MyFloat")] out float MyFloat_Out){
            float __auto_0 = this.MyFloat;
            float MyFloat_Out_1 = __auto_0;
            MyFloat_Out = MyFloat_Out_1;
            return this;
        }
        [return: n6.Pin(IsState = true)]
        public n4.MyRecord_R __Create__([n6.Pin(Name = @"Node Context", Visibility = n7.PinVisibility.Hidden)] n8.NodeContext Node_Context, [n6.Pin(Name = @"MyFloat")] float MyFloat_In){
            n11.RuntimeHelpers.EnsureSufficientExecutionStack();
            float __auto_0 = MyFloat_In;
            n4.MyRecord_R that_1 = this;
            this.MyFloat = MyFloat_In;
            return that_1;
        }
        [return: n6.Pin(IsState = true)]
        [n6.Smell(n8.SymbolSmell.Hidden)]
        public n4.MyRecord_R __CreateDefault__(){
            n4.MyRecord_R that_0 = this;
            this.MyFloat = 0f;
            return that_0;
        }
        public void Dispose(){
            return;
        }
        [n1.Element(DocumentId = @"EKYvICxF27COKXc5f2ctJU", PersistentId = @"Lb4ygQuqotxPdSBJnIM6Bm")]
        [n12.CustomMetaDataAttribute(@"MyKey", @"MyValue")]
        [n12.TagAttribute(@"testtag")]
        public float MyFloat;
        
        void n2.IDisposable.Dispose(){
            using var __current_app_host = __GetAppHost__().MakeCurrentIfNone();
            Dispose();
        }
        public MyRecord_R() : base()
        {
        }
        public MyRecord_R(n8.NodeContext context, uint identity) : base(context, identity)
        {
        }
        public MyRecord_R(MyRecord_R other) : base(other)
        {
            this.MyFloat = other.MyFloat;
        }
        protected override n8.IVLObject __With__(n13.IReadOnlyDictionary<string, n2.Object> values){
            return __WITH__(n10.CompilationHelper.GetValueOrExisting(values, @"MyFloat", in this.MyFloat));
        }
        internal MyRecord_R __WITH__(float MyFloat){
            n4.MyRecord_R that_0 = this;
            that_0 = MyFloat != this.MyFloat ? new n4.MyRecord_R(this) { MyFloat = MyFloat } : that_0;
            return that_0;
        }
        protected override n2.Object __ReadProperty__(string key){
            if (key == "MyFloat") return this.MyFloat;
            return null;
        }
    }
    [n1.Element(DocumentId = @"EKYvICxF27COKXc5f2ctJU", PersistentId = @"CCsK7ereJ3QNbutsX95rnL")]
    [n2.Serializable]
    [n3.Name(@"MyTestClass")]
    public sealed class MyTestClass_C : n5.PatchedObject<n4.MyTestClass_C>, n4.MyCustomSlot_I, n2.IDisposable
    {
        [return: n6.Pin(IsState = true)]
        [n9.CreateNew]
        [n1.Element(DocumentId = @"EKYvICxF27COKXc5f2ctJU", PersistentId = @"Vq81jFQIeWcOXQFU8w7xip")]
        [n3.ShowCategory(true)]
        public static n4.MyTestClass_C Create([n6.Pin(Name = @"Node Context", Visibility = n7.PinVisibility.Hidden)] n8.NodeContext Node_Context){
            Node_Context = Node_Context.WithDefinitionId(@"EKYvICxF27COKXc5f2ctJU", @"CCsK7ereJ3QNbutsX95rnL").WithIsImmutable(false);
            var instance = new MyTestClass_C(Node_Context, n5.PatchedObject.NewIdentity());
            return instance.__Create__(Node_Context);
        }
        [n2.ThreadStatic]
        private static n4.MyTestClass_C __instanceBeingConstructed__;
        [return: n6.Pin(IsState = true)]
        [n9.CreateDefault]
        [n1.Element()]
        [n3.ShowCategory(true)]
        [n6.Smell(n8.SymbolSmell.Hidden)]
        public static n4.MyTestClass_C CreateDefault(){
            if (__instanceBeingConstructed__ != null)
            {
                return n10.CompilationHelper.ReportRecursive(__instanceBeingConstructed__);
            }
            try
            {
                var context = n8.NodeContext.CurrentRoot.WithDefinitionId(@"EKYvICxF27COKXc5f2ctJU", @"CCsK7ereJ3QNbutsX95rnL").WithIsImmutable(false);
                var instance = new MyTestClass_C(context, n5.PatchedObject.NewIdentity());
                __instanceBeingConstructed__ = instance;
                return instance.__CreateDefault__();
            }
            finally
            {
                __instanceBeingConstructed__ = null;
            }
        }
        [return: n6.Pin(IsState = true)]
        [n1.Element(DocumentId = @"EKYvICxF27COKXc5f2ctJU", PersistentId = @"HvGPsMiQsQYNhOhGWW0SDK")]
        [n3.ShowCategory(true)]
        public n4.MyTestClass_C Update(){
            return this;
        }
        [return: n6.Pin(IsState = true)]
        [n1.Element(DocumentId = @"EKYvICxF27COKXc5f2ctJU", PersistentId = @"SU6wkZz1Jb0NkHI3n7ShdC")]
        [n3.ShowCategory(true)]
        public n4.MyTestClass_C OnActivation(){
            return this;
        }
        [return: n6.Pin(IsState = true)]
        [n1.Element(DocumentId = @"EKYvICxF27COKXc5f2ctJU", PersistentId = @"OEfvEEn0JiVMfEkS2Apvaq")]
        [n3.ShowCategory(true)]
        public n4.MyTestClass_C OnDeactivation(){
            return this;
        }
        [return: n6.Pin(IsState = true)]
        public n4.MyTestClass_C __Create__([n6.Pin(Name = @"Node Context", Visibility = n7.PinVisibility.Hidden)] n8.NodeContext Node_Context){
            n11.RuntimeHelpers.EnsureSufficientExecutionStack();
            return this;
        }
        /// <summary>interface operation summary: Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
        [return: n6.Pin(IsState = true)]
        [n1.Element(DocumentId = @"EKYvICxF27COKXc5f2ctJU", PersistentId = @"PDKLfrTRCsXPWHe8xbDpnN")]
        [n3.ShowCategory(true)]
        [n3.Name(@"Dispose")]
        public n4.MyTestClass_C Dispose_(){
            return this;
        }
        [return: n6.Pin(IsState = true)]
        [n6.Smell(n8.SymbolSmell.Hidden)]
        public n4.MyTestClass_C __CreateDefault__(){
            return this;
        }
        void n2.IDisposable.Dispose(){
            using var __current_app_host = __GetAppHost__().MakeCurrentIfNone();
            var __returnValue__ = Dispose_();
        }
        public MyTestClass_C() : base()
        {
        }
        public MyTestClass_C(n8.NodeContext context, uint identity) : base(context, identity)
        {
        }
        public MyTestClass_C(MyTestClass_C other) : base(other)
        {
        }
        protected override n8.IVLObject __With__(n13.IReadOnlyDictionary<string, n2.Object> values){
            return __WITH__();
        }
        internal MyTestClass_C __WITH__(){
            return this;
        }
        protected override n2.Object __ReadProperty__(string key){
            return null;
        }
    }
    [n1.Element(DocumentId = @"EKYvICxF27COKXc5f2ctJU", PersistentId = @"Ks3DYhvYG4SMQdYCFwtLnR")]
    [n3.Name(@"MyCustomSlot")]
    public interface MyCustomSlot_I : n14.ISceneClip, n8.IVLObject
    {
    }
    [n1.Element(DocumentId = @"EKYvICxF27COKXc5f2ctJU", PersistentId = @"SqlmNF6xKBuLjplpZfhGnP")]
    [n2.Serializable]
    [n3.Name(@"SampleRecordandClassApplication")]
    [n6.Category(@"Main")]
    [n6.Smell(n8.SymbolSmell.Hidden)]
    public sealed class SampleRecordandClassApplication_P : n5.PatchedObject<n4.SampleRecordandClassApplication_P>, n2.IDisposable
    {
        [return: n6.Pin(IsState = true)]
        [n9.CreateNew]
        [n1.Element(DocumentId = @"EKYvICxF27COKXc5f2ctJU", PersistentId = @"F9LJYph4a6lMa6AZFBXTmM")]
        [n3.ShowCategory(true)]
        [n6.Smell(n8.SymbolSmell.Hidden)]
        public static n4.SampleRecordandClassApplication_P Create([n6.Pin(Name = @"Node Context", Visibility = n7.PinVisibility.Hidden)] n8.NodeContext Node_Context){
            Node_Context = Node_Context.WithDefinitionId(@"EKYvICxF27COKXc5f2ctJU", @"SqlmNF6xKBuLjplpZfhGnP");
            var instance = new SampleRecordandClassApplication_P(Node_Context, n5.PatchedObject.NewIdentity());
            return instance.__Create__(Node_Context);
        }
        [n2.ThreadStatic]
        private static n4.SampleRecordandClassApplication_P __instanceBeingConstructed__;
        [return: n6.Pin(IsState = true)]
        [n9.CreateDefault]
        [n1.Element()]
        [n3.ShowCategory(true)]
        [n6.Smell(n8.SymbolSmell.Hidden)]
        public static n4.SampleRecordandClassApplication_P CreateDefault(){
            if (__instanceBeingConstructed__ != null)
            {
                return n10.CompilationHelper.ReportRecursive(__instanceBeingConstructed__);
            }
            try
            {
                var context = n8.NodeContext.CurrentRoot.WithDefinitionId(@"EKYvICxF27COKXc5f2ctJU", @"SqlmNF6xKBuLjplpZfhGnP");
                var instance = new SampleRecordandClassApplication_P(context, n5.PatchedObject.NewIdentity());
                __instanceBeingConstructed__ = instance;
                return instance.__CreateDefault__();
            }
            finally
            {
                __instanceBeingConstructed__ = null;
            }
        }
        [return: n6.Pin(IsState = true)]
        [n1.Element(DocumentId = @"EKYvICxF27COKXc5f2ctJU", PersistentId = @"JwcwZIq9F6qNKSW0tle0Al")]
        [n3.ShowCategory(true)]
        [n6.Smell(n8.SymbolSmell.Hidden)]
        public n4.SampleRecordandClassApplication_P Update(){
            var KeepAppAlive_0 = this.__p_AbaAqsCkWIDNBUGYBgi9Nn;
            bool Is_Alive_1 = true;
            KeepAppAlive_0 = KeepAppAlive_0.Update(Is_Alive_In: Is_Alive_1);
            n4.SampleRecordandClassApplication_P that_2 = this;
            if (this.__GetContext__().IsImmutable)
            {
                that_2 = !Equals(KeepAppAlive_0, this.__p_AbaAqsCkWIDNBUGYBgi9Nn) ? new n4.SampleRecordandClassApplication_P(this) { __p_AbaAqsCkWIDNBUGYBgi9Nn = KeepAppAlive_0 } : that_2;
            }
            else
            {
                this.__p_AbaAqsCkWIDNBUGYBgi9Nn = KeepAppAlive_0;
            }
            return that_2;
        }
        [return: n6.Pin(IsState = true)]
        [n6.Smell(n8.SymbolSmell.Hidden)]
        public n4.SampleRecordandClassApplication_P __Create__([n6.Pin(Name = @"Node Context", Visibility = n7.PinVisibility.Hidden)] n8.NodeContext Node_Context){
            n11.RuntimeHelpers.EnsureSufficientExecutionStack();
            var nc_1 = Node_Context;
            var KeepAppAlive_0 = this.__p_AbaAqsCkWIDNBUGYBgi9Nn;
            n8.NodeContext Node_Context_2 = nc_1.CreateSubContext(@"EKYvICxF27COKXc5f2ctJU", @"AbaAqsCkWIDNBUGYBgi9Nn");
            KeepAppAlive_0 = n15.KeepAppAlive_P.Create(Node_Context: Node_Context_2);
            n4.SampleRecordandClassApplication_P that_3 = this;
            this.__p_AbaAqsCkWIDNBUGYBgi9Nn = KeepAppAlive_0;
            return that_3;
        }
        [return: n6.Pin(IsState = true)]
        [n6.Smell(n8.SymbolSmell.Hidden)]
        public n4.SampleRecordandClassApplication_P __CreateDefault__(){
            n4.SampleRecordandClassApplication_P that_0 = this;
            this.__p_AbaAqsCkWIDNBUGYBgi9Nn = n15.KeepAppAlive_P.CreateDefault();
            return that_0;
        }
        public void Dispose(){
            try
            {
                return;
            }
            finally
            {
                n10.CompilationHelper.ShieldedDisposeForManagedFields(this.__p_AbaAqsCkWIDNBUGYBgi9Nn);
            }
        }
        [n1.Element(DocumentId = @"EKYvICxF27COKXc5f2ctJU", PersistentId = @"AbaAqsCkWIDNBUGYBgi9Nn", IsManaged = true, IsAutoGenerated = true)]
        public n15.KeepAppAlive_P __p_AbaAqsCkWIDNBUGYBgi9Nn;
        
        void n2.IDisposable.Dispose(){
            using var __current_app_host = __GetAppHost__().MakeCurrentIfNone();
            Dispose();
        }
        public SampleRecordandClassApplication_P() : base()
        {
        }
        public SampleRecordandClassApplication_P(n8.NodeContext context, uint identity) : base(context, identity)
        {
        }
        public SampleRecordandClassApplication_P(SampleRecordandClassApplication_P other) : base(other)
        {
            this.__p_AbaAqsCkWIDNBUGYBgi9Nn = other.__p_AbaAqsCkWIDNBUGYBgi9Nn;
        }
        protected override n8.IVLObject __With__(n13.IReadOnlyDictionary<string, n2.Object> values){
            return __WITH__(n10.CompilationHelper.GetValueOrExisting(values, @"__p_AbaAqsCkWIDNBUGYBgi9Nn", in this.__p_AbaAqsCkWIDNBUGYBgi9Nn));
        }
        internal SampleRecordandClassApplication_P __WITH__(n15.KeepAppAlive_P __p_AbaAqsCkWIDNBUGYBgi9Nn){
            n4.SampleRecordandClassApplication_P that_0 = this;
            if (this.__GetContext__().IsImmutable)
            {
                that_0 = !Equals(__p_AbaAqsCkWIDNBUGYBgi9Nn, this.__p_AbaAqsCkWIDNBUGYBgi9Nn) ? new n4.SampleRecordandClassApplication_P(this) { __p_AbaAqsCkWIDNBUGYBgi9Nn = __p_AbaAqsCkWIDNBUGYBgi9Nn } : that_0;
            }
            else
            {
                this.__p_AbaAqsCkWIDNBUGYBgi9Nn = __p_AbaAqsCkWIDNBUGYBgi9Nn;
            }
            return that_0;
        }
        protected override n2.Object __ReadProperty__(string key){
            if (key == "__p_AbaAqsCkWIDNBUGYBgi9Nn") return this.__p_AbaAqsCkWIDNBUGYBgi9Nn;
            return null;
        }
    }
}
namespace _SampleRecordandClass_.__auto
{
}
namespace _SampleRecordandClass_
{
    public struct __AdaptiveImplementations__EKYvICxF27COKXc5f2ctJU
    {
    }
    public static class __AdaptiveNodeLookup__EKYvICxF27COKXc5f2ctJU
    {
        public static void CreateDefault(out n4.MyRecord_R Output_Out){
            var Output_0 = n4.MyRecord_R.CreateDefault();
            Output_Out = Output_0;
            return;
        }
        public static void CreateDefault(out n4.MyTestClass_C Output_Out){
            var Output_0 = n4.MyTestClass_C.CreateDefault();
            Output_Out = Output_0;
            return;
        }
        public static void CreateDefault(out n4.MyCustomSlot_I Output_Out){
            n16._Operations_.CreateDefault_Generic<n4.MyCustomSlot_I>(Output_Out: out n4.MyCustomSlot_I Output_0);
            Output_Out = Output_0;
            return;
        }
    }
}
