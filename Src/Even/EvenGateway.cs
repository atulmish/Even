﻿using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Even.Messages;
using System.Diagnostics.Contracts;

namespace Even
{
    public class EvenGateway 
    {
        public EvenGateway(EvenServices services, GlobalOptions options)
        {
            Argument.RequiresNotNull(services, nameof(services));
            Argument.RequiresNotNull(options, nameof(options));

            this.Services = services;
            this._options = options;
        }

        public EvenServices Services { get; }
        GlobalOptions _options;

        /// <summary>
        /// Sends a command to an aggregate using the aggregate's category as the stream id.
        /// </summary>
        public Task SendAggregateCommand<T>(object command, TimeSpan? timeout = null)
            where T : Aggregate
        {
            var streamId = ESCategoryAttribute.GetCategory(typeof(T));
            return SendAggregateCommand<T>(streamId, command, timeout);
        }

        /// <summary>
        /// Sends a command to an aggregate using the aggregate's category and an id to compose the stream id.
        /// The stream will be generated as "category-id".
        /// </summary>
        public Task<CommandResult> SendAggregateCommand<T>(object id, object command, TimeSpan? timeout = null)
            where T : Aggregate
        {
            Contract.Requires(id != null);

            var category = ESCategoryAttribute.GetCategory(typeof(T));
            var streamId = id != null ? category + "-" + id.ToString() : category;

            return SendAggregateCommand<T>(streamId, command, timeout);
        }

        /// <summary>
        /// Sends a command to an aggregate using the specified stream id.
        /// </summary>
        public Task<CommandResult> SendAggregateCommand<T>(string streamId, object command, TimeSpan? timeout = null)
            where T : Aggregate
        {
            Argument.RequiresNotNull(streamId, nameof(streamId));
            Argument.RequiresNotNull(command, nameof(command));

            var to = timeout ?? _options.DefaultCommandTimeout;

            var aggregateCommand = new AggregateCommand(streamId, command, to);
            var envelope = new AggregateCommandEnvelope(typeof(T), aggregateCommand);

            // TODO: add some threshold to Ask higher than the timeout
            return Ask(Services.Aggregates, envelope, to);
        }

        private static async Task<CommandResult> Ask(IActorRef actor, object msg, TimeSpan timeout)
        {
            object response;

            try
            {
                response = (CommandResponse)await actor.Ask(msg, timeout);
            }
            catch (TaskCanceledException)
            {
                throw new CommandException("Command timeout");
            }

            if (response is CommandSucceeded)
                return new CommandResult();

            if (response is CommandRejected)
                return new CommandResult(((CommandRejected)response).Reasons);

            if (response is CommandFailed)
            {
                var cf = (CommandFailed)response;
                throw new CommandException("An error occoured while processing the command: " + cf.Reason, cf.Exception);
            }

            throw new UnexpectedCommandResponseException(response);
        }
    }
}
