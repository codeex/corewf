// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

namespace System.Activities.Statements
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Globalization;
    using System.Activities.Runtime;
    using System.Windows.Markup;

#if NET45
    using System.Activities.DynamicUpdate; 
#endif

    //[SuppressMessage(FxCop.Category.Naming, FxCop.Rule.IdentifiersShouldNotHaveIncorrectSuffix,
    //    Justification = "Approved Workflow naming")]
    [ContentProperty("Delegate")]
    public sealed class InvokeDelegate : NativeActivity
    {
        private readonly IDictionary<string, Argument> delegateArguments;
        private bool hasOutputArguments;

        public InvokeDelegate()
        {
            this.delegateArguments = new Dictionary<string, Argument>();
        }

        [DefaultValue(null)]
        public ActivityDelegate Delegate
        {
            get;
            set;
        }

        public IDictionary<string, Argument> DelegateArguments
        {
            get
            {
                return this.delegateArguments;
            }
        }

        [DefaultValue(null)]
        public Activity Default
        {
            get;
            set;
        }

#if NET45
        protected override void OnCreateDynamicUpdateMap(NativeActivityUpdateMapMetadata metadata, Activity originalActivity)
        {
            metadata.AllowUpdateInsideThisActivity();
        } 
#endif

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            Collection<RuntimeArgument> arguments = new Collection<RuntimeArgument>();

            foreach (KeyValuePair<string, Argument> entry in this.DelegateArguments)
            {
                RuntimeArgument argument = new RuntimeArgument(entry.Key, entry.Value.ArgumentType, entry.Value.Direction);
                metadata.Bind(entry.Value, argument);
                arguments.Add(argument);
            }

            metadata.SetArgumentsCollection(arguments);
            metadata.AddDelegate(this.Delegate);

            if (this.Delegate != null)
            {
                IList<RuntimeDelegateArgument> targetDelegateArguments = this.Delegate.RuntimeDelegateArguments;
                if (this.DelegateArguments.Count != targetDelegateArguments.Count)
                {
                    metadata.AddValidationError(SR.WrongNumberOfArgumentsForActivityDelegate);
                }

                // Validate that the names and directionality of arguments in DelegateArguments dictionary 
                // match the names and directionality of arguments returned by the ActivityDelegate.GetDelegateParameters 
                // call above. 
                for (int i = 0; i < targetDelegateArguments.Count; i++)
                {
                    RuntimeDelegateArgument expectedParameter = targetDelegateArguments[i];
                    string parameterName = expectedParameter.Name;
                    if (this.DelegateArguments.TryGetValue(parameterName, out Argument delegateArgument))
                    {
                        if (delegateArgument.Direction != expectedParameter.Direction)
                        {
                            metadata.AddValidationError(SR.DelegateParameterDirectionalityMismatch(parameterName, delegateArgument.Direction, expectedParameter.Direction));
                        }

                        if (expectedParameter.Direction == ArgumentDirection.In)
                        {
                            if (!TypeHelper.AreTypesCompatible(delegateArgument.ArgumentType, expectedParameter.Type))
                            {
                                metadata.AddValidationError(SR.DelegateInArgumentTypeMismatch(parameterName, expectedParameter.Type, delegateArgument.ArgumentType));
                            }
                        }
                        else
                        {
                            if (!TypeHelper.AreTypesCompatible(expectedParameter.Type, delegateArgument.ArgumentType))
                            {
                                metadata.AddValidationError(SR.DelegateOutArgumentTypeMismatch(parameterName, expectedParameter.Type, delegateArgument.ArgumentType));
                            }
                        }
                    }
                    else
                    {
                        metadata.AddValidationError(SR.InputParametersMissing(expectedParameter.Name));
                    }

                    if (!this.hasOutputArguments && ArgumentDirectionHelper.IsOut(expectedParameter.Direction))
                    {
                        this.hasOutputArguments = true;
                    }
                }
            }

            metadata.AddChild(this.Default);
        }

        protected override void Execute(NativeActivityContext context)
        {
            if (Delegate == null || Delegate.Handler == null)
            {
                if (this.Default != null)
                {
                    context.ScheduleActivity(this.Default);
                }

                return;
            }

            Dictionary<string, object> inputParameters = new Dictionary<string, object>();

            if (DelegateArguments.Count > 0)
            {
                foreach (KeyValuePair<string, Argument> entry in DelegateArguments)
                {
                    if (ArgumentDirectionHelper.IsIn(entry.Value.Direction))
                    {
                        inputParameters.Add(entry.Key, entry.Value.Get(context));
                    }
                }
            }

            context.ScheduleDelegate(Delegate, inputParameters, new DelegateCompletionCallback(OnHandlerComplete), null);
        }

        private void OnHandlerComplete(NativeActivityContext context, ActivityInstance completedInstance, IDictionary<string, object> outArguments)
        {
            if (this.hasOutputArguments)
            {
                foreach (KeyValuePair<string, object> entry in outArguments)
                {
                    if (DelegateArguments.TryGetValue(entry.Key, out Argument argument))
                    {
                        if (ArgumentDirectionHelper.IsOut(argument.Direction))
                        {
                            DelegateArguments[entry.Key].Set(context, entry.Value);
                        }
                        else
                        {
                            Fx.Assert(string.Format(CultureInfo.InvariantCulture, "Expected argument named '{0}' in the DelegateArguments collection to be an out argument.", entry.Key));
                        }
                    }
                }
            }
        }

    }
}
