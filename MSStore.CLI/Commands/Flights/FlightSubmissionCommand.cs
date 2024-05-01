// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.CommandLine;

namespace MSStore.CLI.Commands.Flights
{
    internal class FlightSubmissionCommand : Command
    {
        public FlightSubmissionCommand()
            : base("submission", "Execute flight submissions related tasks.")
        {
            AddCommand(new Submission.GetCommand());
            AddCommand(new Submission.DeleteCommand());
            AddCommand(new Submission.UpdateCommand());
            AddCommand(new Submission.PublishCommand());
            AddCommand(new Submission.PollCommand());
            this.SetDefaultHelpHandler();
        }
    }
}
