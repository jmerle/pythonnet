using System;
using System.Collections;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
using System.Linq;

namespace Python.Runtime
{
    /// <summary>
    /// A MethodBinder encapsulates information about a (possibly overloaded)
    /// managed method, and is responsible for selecting the right method given
    /// a set of Python arguments. This is also used as a base class for the
    /// ConstructorBinder, a minor variation used to invoke constructors.
    /// </summary>
    internal class MethodBinder
    {
        private List<MethodInformation> list;
        public bool init = false;
        public bool allow_threads = true;

        internal MethodBinder()
        {
            list = new List<MethodInformation>();
        }

        internal MethodBinder(MethodInfo mi)
        {
            list = new List<MethodInformation> { new MethodInformation(mi, mi.GetParameters()) };
        }

        public int Count
        {
            get { return list.Count; }
        }

        internal void AddMethod(MethodBase m)
        {
            // we added a new method so we have to re sort the method list
            init = false;
            list.Add(new MethodInformation(m, m.GetParameters()));
        }

        /// <summary>
        /// Given a sequence of MethodInfo and a sequence of types, return the
        /// MethodInfo that matches the signature represented by those types.
        /// </summary>
        internal static MethodInfo MatchSignature(MethodInfo[] mi, Type[] tp)
        {
            if (tp == null)
            {
                return null;
            }
            int count = tp.Length;
            foreach (MethodInfo t in mi)
            {
                ParameterInfo[] pi = t.GetParameters();
                if (pi.Length != count)
                {
                    continue;
                }
                for (var n = 0; n < pi.Length; n++)
                {
                    if (tp[n] != pi[n].ParameterType)
                    {
                        break;
                    }
                    if (n == pi.Length - 1)
                    {
                        return t;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Given a sequence of MethodInfo and a sequence of type parameters,
        /// return the MethodInfo that represents the matching closed generic.
        /// </summary>
        internal static MethodInfo MatchParameters(MethodInfo[] mi, Type[] tp)
        {
            if (tp == null)
            {
                return null;
            }
            int count = tp.Length;
            foreach (MethodInfo t in mi)
            {
                if (!t.IsGenericMethodDefinition)
                {
                    continue;
                }
                Type[] args = t.GetGenericArguments();
                if (args.Length != count)
                {
                    continue;
                }
                return t.MakeGenericMethod(tp);
            }
            return null;
        }


        /// <summary>
        /// Given a sequence of MethodInfo and two sequences of type parameters,
        /// return the MethodInfo that matches the signature and the closed generic.
        /// </summary>
        internal static MethodInfo MatchSignatureAndParameters(MethodInfo[] mi, Type[] genericTp, Type[] sigTp)
        {
            if (genericTp == null || sigTp == null)
            {
                return null;
            }
            int genericCount = genericTp.Length;
            int signatureCount = sigTp.Length;
            foreach (MethodInfo t in mi)
            {
                if (!t.IsGenericMethodDefinition)
                {
                    continue;
                }
                Type[] genericArgs = t.GetGenericArguments();
                if (genericArgs.Length != genericCount)
                {
                    continue;
                }
                ParameterInfo[] pi = t.GetParameters();
                if (pi.Length != signatureCount)
                {
                    continue;
                }
                for (var n = 0; n < pi.Length; n++)
                {
                    if (sigTp[n] != pi[n].ParameterType)
                    {
                        break;
                    }
                    if (n == pi.Length - 1)
                    {
                        MethodInfo match = t;
                        if (match.IsGenericMethodDefinition)
                        {
                            // FIXME: typeArgs not used
                            Type[] typeArgs = match.GetGenericArguments();
                            return match.MakeGenericMethod(genericTp);
                        }
                        return match;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Return the array of MethodInfo for this method. The result array
        /// is arranged in order of precedence (done lazily to avoid doing it
        /// at all for methods that are never called).
        /// </summary>
        internal List<MethodInformation> GetMethods()
        {
            if (!init)
            {
                // I'm sure this could be made more efficient.
                list.Sort(new MethodSorter());
                init = true;
            }
            return list;
        }

        /// <summary>
        /// Precedence algorithm largely lifted from Jython - the concerns are
        /// generally the same so we'll start with this and tweak as necessary.
        /// </summary>
        /// <remarks>
        /// Based from Jython `org.python.core.ReflectedArgs.precedence`
        /// See: https://github.com/jythontools/jython/blob/master/src/org/python/core/ReflectedArgs.java#L192
        /// </remarks>
        private static int GetPrecedence(MethodInformation methodInformation)
        {
            ParameterInfo[] pi = methodInformation.ParameterInfo;
            var mi = methodInformation.MethodBase;
            int val = mi.IsStatic ? 3000 : 0;
            int num = pi.Length;

            val += mi.IsGenericMethod ? 1 : 0;
            for (var i = 0; i < num; i++)
            {
                val += ArgPrecedence(pi[i].ParameterType);
            }

            var info = mi as MethodInfo;
            if (info != null)
            {
                val += ArgPrecedence(info.ReturnType);
                val += mi.DeclaringType == mi.ReflectedType ? 0 : 3000;
            }

            return val;
        }

        /// <summary>
        /// Return a precedence value for a particular Type object.
        /// </summary>
        internal static int ArgPrecedence(Type t)
        {
            Type objectType = typeof(object);
            if (t == objectType)
            {
                return 3000;
            }

            if (t.IsAssignableFrom(typeof(PyObject)))
            {
                return -1;
            }

            TypeCode tc = Type.GetTypeCode(t);
            // TODO: Clean up
            switch (tc)
            {
                case TypeCode.Object:
                    return 1;

                case TypeCode.UInt64:
                    return 10;

                case TypeCode.UInt32:
                    return 11;

                case TypeCode.UInt16:
                    return 12;

                case TypeCode.Int64:
                    return 13;

                case TypeCode.Int32:
                    return 14;

                case TypeCode.Int16:
                    return 15;

                case TypeCode.Char:
                    return 16;

                case TypeCode.SByte:
                    return 17;

                case TypeCode.Byte:
                    return 18;

                case TypeCode.Single:
                    return 20;

                case TypeCode.Double:
                    return 21;

                case TypeCode.String:
                    return 30;

                case TypeCode.Boolean:
                    return 40;
            }

            if (t.IsArray)
            {
                Type e = t.GetElementType();
                if (e == objectType)
                {
                    return 2500;
                }
                return 100 + ArgPrecedence(e);
            }

            return 2000;
        }

        /// <summary>
        /// Bind the given Python instance and arguments to a particular method
        /// overload and return a structure that contains the converted Python
        /// instance, converted arguments and the correct method to call.
        /// </summary>
        internal Binding Bind(IntPtr inst, IntPtr args, IntPtr kw)
        {
            return Bind(inst, args, kw, null, null);
        }

        internal Binding Bind(IntPtr inst, IntPtr args, IntPtr kw, MethodBase info)
        {
            return Bind(inst, args, kw, info, null);
        }

        internal Binding Bind(IntPtr inst, IntPtr args, IntPtr kw, MethodBase info, MethodInfo[] methodinfo)
        {
            // loop to find match, return invoker w/ or /wo error

            var kwargDict = new Dictionary<string, IntPtr>();
            if (kw != IntPtr.Zero)
            {
                var pynkwargs = (int) Runtime.PyDict_Size(kw);
                IntPtr keylist = Runtime.PyDict_Keys(kw);
                IntPtr valueList = Runtime.PyDict_Values(kw);
                for (int i = 0; i < pynkwargs; ++i)
                {
                    var keyStr = Runtime.GetManagedString(Runtime.PyList_GetItem(keylist, i));
                    kwargDict[keyStr] = Runtime.PyList_GetItem(valueList, i).DangerousGetAddress();
                }

                Runtime.XDecref(keylist);
                Runtime.XDecref(valueList);
            }

            var pynargs = (int) Runtime.PyTuple_Size(args);
            object arg;
            var isGeneric = false;
            ArrayList defaultArgList;
            Type clrtype;
            Binding bindingUsingImplicitConversion = null;

            var methods = info == null
                ? GetMethods()
                : new List<MethodInformation>(1) {new MethodInformation(info, info.GetParameters())};

            // TODO: Clean up
            foreach (var methodInformation in methods)
            {
                var mi = methodInformation.MethodBase;
                var pi = methodInformation.ParameterInfo;

                if (mi.IsGenericMethod)
                {
                    isGeneric = true;
                }

                bool paramsArray;

                if (!MatchesArgumentCount(pynargs, pi, kwargDict, out paramsArray, out defaultArgList))
                {
                    continue;
                }

                int outs;
                bool usedImplicitConversion;

                var margs = TryConvertArguments(pi, paramsArray, args, pynargs, kwargDict, defaultArgList,
                    needsResolution: methods.Count > 1,
                    outs: out outs,
                    usedImplicitConversion: out usedImplicitConversion);

                if (margs == null)
                {
                    continue;
                }

                object target = null;
                if (!mi.IsStatic && inst != IntPtr.Zero)
                {
                    //CLRObject co = (CLRObject)ManagedType.GetManagedObject(inst);
                    // InvalidCastException: Unable to cast object of type
                    // 'Python.Runtime.ClassObject' to type 'Python.Runtime.CLRObject'
                    var co = ManagedType.GetManagedObject(inst) as CLRObject;

                    // Sanity check: this ensures a graceful exit if someone does
                    // something intentionally wrong like call a non-static method
                    // on the class rather than on an instance of the class.
                    // XXX maybe better to do this before all the other rigmarole.
                    if (co == null)
                    {
                        return null;
                    }

                    target = co.inst;
                }

                var binding = new Binding(mi, target, margs, outs);
                if (usedImplicitConversion)
                {
                    // lets just keep the first binding using implicit conversion
                    // this is to respect method order/precedence
                    if (bindingUsingImplicitConversion == null)
                    {
                        // in this case we will not return the binding yet in case there is a match
                        // which does not use implicit conversions, which will return directly
                        bindingUsingImplicitConversion = binding;
                    }
                }
                else
                {
                    return binding;
                }
            }

            // if we generated a binding using implicit conversion return it
            if (bindingUsingImplicitConversion != null)
            {
                return bindingUsingImplicitConversion;
            }

            // We weren't able to find a matching method but at least one
            // is a generic method and info is null. That happens when a generic
            // method was not called using the [] syntax. Let's introspect the
            // type of the arguments and use it to construct the correct method.
            if (isGeneric && info == null && methodinfo != null)
            {
                Type[] types = Runtime.PythonArgsToTypeArray(args, true);
                MethodInfo mi = MatchParameters(methodinfo, types);
                return Bind(inst, args, kw, mi, null);
            }
            return null;
        }

        /// <summary>
        /// Attempts to convert Python positional argument tuple and keyword argument table
        /// into an array of managed objects, that can be passed to a method.
        /// </summary>
        /// <param name="pi">Information about expected parameters</param>
        /// <param name="paramsArray"><c>true</c>, if the last parameter is a params array.</param>
        /// <param name="args">A pointer to the Python argument tuple</param>
        /// <param name="pyArgCount">Number of arguments, passed by Python</param>
        /// <param name="kwargDict">Dictionary of keyword argument name to python object pointer</param>
        /// <param name="defaultArgList">A list of default values for omitted parameters</param>
        /// <param name="needsResolution"><c>true</c>, if overloading resolution is required</param>
        /// <param name="outs">Returns number of output parameters</param>
        /// <returns>An array of .NET arguments, that can be passed to a method.</returns>
        static object[] TryConvertArguments(ParameterInfo[] pi, bool paramsArray,
            IntPtr args, int pyArgCount,
            Dictionary<string, IntPtr> kwargDict,
            ArrayList defaultArgList,
            bool needsResolution,
            out int outs,
            out bool usedImplicitConversion)
        {
            outs = 0;
            usedImplicitConversion = false;
            var margs = new object[pi.Length];
            int arrayStart = paramsArray ? pi.Length - 1 : -1;

            for (int paramIndex = 0; paramIndex < pi.Length; paramIndex++)
            {
                var parameter = pi[paramIndex];
                bool hasNamedParam = kwargDict.ContainsKey(parameter.Name);

                if (paramIndex >= pyArgCount && !hasNamedParam)
                {
                    if (defaultArgList != null)
                    {
                        margs[paramIndex] = defaultArgList[paramIndex - pyArgCount];
                    }

                    continue;
                }

                IntPtr op;
                if (hasNamedParam)
                {
                    op = kwargDict[parameter.Name];
                }
                else
                {
                    op = (arrayStart == paramIndex)
                        // map remaining Python arguments to a tuple since
                        // the managed function accepts it - hopefully :]
                        ? Runtime.PyTuple_GetSlice(args, arrayStart, pyArgCount)
                        : Runtime.PyTuple_GetItem(args, paramIndex);
                }

                bool isOut;
                if (!TryConvertArgument(op, parameter.ParameterType, needsResolution, out margs[paramIndex], out isOut, out usedImplicitConversion))
                {
                    return null;
                }

                if (arrayStart == paramIndex)
                {
                    // TODO: is this a bug? Should this happen even if the conversion fails?
                    // GetSlice() creates a new reference but GetItem()
                    // returns only a borrow reference.
                    Runtime.XDecref(op);
                }

                if (parameter.IsOut || isOut)
                {
                    outs++;
                }
            }

            return margs;
        }

        static bool TryConvertArgument(IntPtr op, Type parameterType, bool needsResolution,
                                       out object arg, out bool isOut, out bool usedImplicitConversion)
        {
            arg = null;
            isOut = false;
            usedImplicitConversion = false;
            var clrtype = TryComputeClrArgumentType(parameterType, op, needsResolution: needsResolution, out usedImplicitConversion);
            if (clrtype == null)
            {
                return false;
            }

            if (!Converter.ToManaged(op, clrtype, out arg, false))
            {
                Exceptions.Clear();
                return false;
            }

            isOut = clrtype.IsByRef;
            return true;
        }

        static Type TryComputeClrArgumentType(Type parameterType, IntPtr argument, bool needsResolution, out bool usedImplicitConversion)
        {
            // this logic below handles cases when multiple overloading methods
            // are ambiguous, hence comparison between Python and CLR types
            // is necessary
            usedImplicitConversion = false;
            Type clrtype = null;
            IntPtr pyoptype;
            if (needsResolution)
            {
                // HACK: each overload should be weighted in some way instead
                pyoptype = Runtime.PyObject_Type(argument);
                Exceptions.Clear();
                if (pyoptype != IntPtr.Zero)
                {
                    clrtype = Converter.GetTypeByAlias(pyoptype);
                }
                Runtime.XDecref(pyoptype);
            }

            if (clrtype != null)
            {
                var typematch = false;
                if ((parameterType != typeof(object)) && (parameterType != clrtype))
                {
                    IntPtr pytype = Converter.GetPythonTypeByAlias(parameterType);
                    pyoptype = Runtime.PyObject_Type(argument);
                    Exceptions.Clear();
                    if (pyoptype != IntPtr.Zero)
                    {
                        if (pytype != pyoptype)
                        {
                            typematch = false;
                        }
                        else
                        {
                            typematch = true;
                            clrtype = parameterType;
                        }
                    }
                    if (!typematch)
                    {
                        // this takes care of nullables
                        var underlyingType = Nullable.GetUnderlyingType(parameterType);
                        if (underlyingType == null)
                        {
                            underlyingType = parameterType;
                        }
                        // this takes care of enum values
                        TypeCode argtypecode = Type.GetTypeCode(underlyingType);
                        TypeCode paramtypecode = Type.GetTypeCode(clrtype);
                        if (argtypecode == paramtypecode)
                        {
                            typematch = true;
                            clrtype = parameterType;
                        }
                        // accepts non-decimal numbers in decimal parameters 
                        if (underlyingType == typeof(decimal))
                        {
                            object arg;
                            clrtype = parameterType;
                            typematch = Converter.ToManaged(argument, clrtype, out arg, false);
                        }
                        // this takes care of implicit conversions
                        var opImplicit = parameterType.GetMethod("op_Implicit", new[] { clrtype });
                        if (opImplicit != null)
                        {
                            usedImplicitConversion = typematch = opImplicit.ReturnType == parameterType;
                            clrtype = parameterType;
                        }
                    }
                    Runtime.XDecref(pyoptype);
                    if (!typematch)
                    {
                        return null;
                    }
                }
                else
                {
                    typematch = true;
                    clrtype = parameterType;
                }
            }
            else
            {
                clrtype = parameterType;
            }

            return clrtype;
        }

        static bool MatchesArgumentCount(int positionalArgumentCount, ParameterInfo[] parameters,
            Dictionary<string, IntPtr> kwargDict,
            out bool paramsArray,
            out ArrayList defaultArgList)
        {
            defaultArgList = null;
            var match = false;
            paramsArray = false;

            if (positionalArgumentCount == parameters.Length)
            {
                match = true;
            }
            else if (positionalArgumentCount < parameters.Length)
            {
                // every parameter past 'positionalArgumentCount' must have either
                // a corresponding keyword argument or a default parameter
                match = true;
                defaultArgList = new ArrayList();
                for (var v = positionalArgumentCount; v < parameters.Length; v++)
                {
                    if (kwargDict.ContainsKey(parameters[v].Name))
                    {
                        // we have a keyword argument for this parameter,
                        // no need to check for a default parameter, but put a null
                        // placeholder in defaultArgList
                        defaultArgList.Add(null);
                    }
                    else if (parameters[v].IsOptional)
                    {
                        // IsOptional will be true if the parameter has a default value,
                        // or if the parameter has the [Optional] attribute specified.
                        // The GetDefaultValue() extension method will return the value
                        // to be passed in as the parameter value
                        defaultArgList.Add(parameters[v].GetDefaultValue());
                    }
                    else
                    {
                        match = false;
                    }
                }
            }
            else if (positionalArgumentCount > parameters.Length && parameters.Length > 0 &&
                       Attribute.IsDefined(parameters[parameters.Length - 1], typeof(ParamArrayAttribute)))
            {
                // This is a `foo(params object[] bar)` style method
                match = true;
                paramsArray = true;
            }

            return match;
        }

        internal virtual IntPtr Invoke(IntPtr inst, IntPtr args, IntPtr kw)
        {
            return Invoke(inst, args, kw, null, null);
        }

        internal virtual IntPtr Invoke(IntPtr inst, IntPtr args, IntPtr kw, MethodBase info)
        {
            return Invoke(inst, args, kw, info, null);
        }

        internal virtual IntPtr Invoke(IntPtr inst, IntPtr args, IntPtr kw, MethodBase info, MethodInfo[] methodinfo)
        {
            Binding binding = Bind(inst, args, kw, info, methodinfo);
            object result;
            IntPtr ts = IntPtr.Zero;

            if (binding == null)
            {
                var value = new StringBuilder("No method matches given arguments");
                if (methodinfo != null && methodinfo.Length > 0)
                {
                    value.Append($" for {methodinfo[0].Name}");
                }

                long argCount = Runtime.PyTuple_Size(args);
                value.Append(": (");
                for(long argIndex = 0; argIndex < argCount; argIndex++) {
                    var arg = Runtime.PyTuple_GetItem(args, argIndex);
                    if (arg != IntPtr.Zero) {
                        var type = Runtime.PyObject_Type(arg);
                        if (type != IntPtr.Zero) {
                            try {
                                var description = Runtime.PyObject_Unicode(type);
                                if (description != IntPtr.Zero) {
                                    value.Append(Runtime.GetManagedString(description));
                                    Runtime.XDecref(description);
                                }
                            } finally {
                                Runtime.XDecref(type);
                            }
                        }
                    }

                    if (argIndex + 1 < argCount)
                        value.Append(", ");
                }
                value.Append(')');
                Exceptions.SetError(Exceptions.TypeError, value.ToString());
                return IntPtr.Zero;
            }

            if (allow_threads)
            {
                ts = PythonEngine.BeginAllowThreads();
            }

            try
            {
                result = binding.info.Invoke(binding.inst, BindingFlags.Default, null, binding.args, null);
            }
            catch (Exception e)
            {
                if (e.InnerException != null)
                {
                    e = e.InnerException;
                }
                if (allow_threads)
                {
                    PythonEngine.EndAllowThreads(ts);
                }
                Exceptions.SetError(e);
                return IntPtr.Zero;
            }

            if (allow_threads)
            {
                PythonEngine.EndAllowThreads(ts);
            }

            // If there are out parameters, we return a tuple containing
            // the result followed by the out parameters. If there is only
            // one out parameter and the return type of the method is void,
            // we return the out parameter as the result to Python (for
            // code compatibility with ironpython).

            var mi = (MethodInfo)binding.info;

            if (binding.outs == 1 && mi.ReturnType == typeof(void))
            {
            }

            if (binding.outs > 0)
            {
                ParameterInfo[] pi = mi.GetParameters();
                int c = pi.Length;
                var n = 0;

                IntPtr t = Runtime.PyTuple_New(binding.outs + 1);
                IntPtr v = Converter.ToPython(result, mi.ReturnType);
                Runtime.PyTuple_SetItem(t, n, v);
                n++;

                for (var i = 0; i < c; i++)
                {
                    Type pt = pi[i].ParameterType;
                    if (pi[i].IsOut || pt.IsByRef)
                    {
                        v = Converter.ToPython(binding.args[i], pt);
                        Runtime.PyTuple_SetItem(t, n, v);
                        n++;
                    }
                }

                if (binding.outs == 1 && mi.ReturnType == typeof(void))
                {
                    v = Runtime.PyTuple_GetItem(t, 1);
                    Runtime.XIncref(v);
                    Runtime.XDecref(t);
                    return v;
                }

                return t;
            }

            return Converter.ToPython(result, mi.ReturnType);
        }


        /// <summary>
        /// Utility class to store the information about a <see cref="MethodBase"/>
        /// </summary>
        internal class MethodInformation
        {
            public MethodBase MethodBase { get; }
            public ParameterInfo[] ParameterInfo { get; }

            public MethodInformation(MethodBase methodBase, ParameterInfo[] parameterInfo)
            {
                MethodBase = methodBase;
                ParameterInfo = parameterInfo;
            }

            public override string ToString()
            {
                return MethodBase.ToString();
            }
        }

        /// <summary>
        /// Utility class to sort method info by parameter type precedence.
        /// </summary>
        private class MethodSorter : IComparer<MethodInformation>
        {
            public int Compare(MethodInformation x, MethodInformation y)
            {
                int p1 = GetPrecedence(x);
                int p2 = GetPrecedence(y);
                if (p1 < p2)
                {
                    return -1;
                }

                if (p1 > p2)
                {
                    return 1;
                }

                return 0;
            }
        }
    }

    /// <summary>
    /// A Binding is a utility instance that bundles together a MethodInfo
    /// representing a method to call, a (possibly null) target instance for
    /// the call, and the arguments for the call (all as managed values).
    /// </summary>
    internal class Binding
    {
        public MethodBase info;
        public object[] args;
        public object inst;
        public int outs;

        internal Binding(MethodBase info, object inst, object[] args, int outs)
        {
            this.info = info;
            this.inst = inst;
            this.args = args;
            this.outs = outs;
        }
    }


    static internal class ParameterInfoExtensions
    {
        public static object GetDefaultValue(this ParameterInfo parameterInfo)
        {
            // parameterInfo.HasDefaultValue is preferable but doesn't exist in .NET 4.0
            bool hasDefaultValue = (parameterInfo.Attributes & ParameterAttributes.HasDefault) ==
                ParameterAttributes.HasDefault;

            if (hasDefaultValue)
            {
                return parameterInfo.DefaultValue;
            }
            else
            {
                // [OptionalAttribute] was specified for the parameter.
                // See https://stackoverflow.com/questions/3416216/optionalattribute-parameters-default-value
                // for rules on determining the value to pass to the parameter
                var type = parameterInfo.ParameterType;
                if (type == typeof(object))
                    return Type.Missing;
                else if (type.IsValueType)
                    return Activator.CreateInstance(type);
                else
                    return null;
            }
        }
    }
}
