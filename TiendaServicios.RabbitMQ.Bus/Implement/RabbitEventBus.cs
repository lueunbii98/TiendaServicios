﻿using MediatR;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TiendaServicios.RabbitMQ.Bus.BusRabbit;
using TiendaServicios.RabbitMQ.Bus.Comandos;
using TiendaServicios.RabbitMQ.Bus.Eventos;

namespace TiendaServicios.RabbitMQ.Bus.Implement
{
    internal class RabbitEventBus : IRabbitEventBus
    {
        private readonly IMediator _mediator;
        private readonly Dictionary<string, List<Type>> _manejadores;
        private readonly List<Type> _eventoTipos;

        public RabbitEventBus(IMediator mediator)
        {
            _mediator = mediator;
            _manejadores = new Dictionary<string, List<Type>>();
            _eventoTipos = new List<Type>(); 
        }
        public Task EnviarComando<T>(T comando) where T : Comando
        {
            return _mediator.Send(comando);
        }

        public void Publish<T>(T evento) where T : Evento
        {
            var factory = new ConnectionFactory() { HostName = "localhost"};
            using (var connection = factory.CreateConnection())
            using (var channel = connection.CreateModel()) {
                var eventoName = evento.GetType().Name;

                channel.QueueDeclare(eventoName, false, false, false, null);

                var message = JsonConvert.SerializeObject(evento);
                var body = Encoding.UTF8.GetBytes(message);

                channel.BasicPublish("", eventoName, null, body);
            }
        }

        public void Subscribe<T, TH>()
            where T : Evento
            where TH : IEventoManejador
        {
            var eventoNombre = typeof(T).Name;
            var manejadorEventoTipo = typeof(TH);

            if (!_eventoTipos.Contains(typeof(T))) {
                _eventoTipos.Add(typeof(T));
            }
            if (!_manejadores.ContainsKey(eventoNombre)) {
                _manejadores.Add(eventoNombre, new List<Type>());
            }

            if (_manejadores[eventoNombre].Any(x => x.GetType() == manejadorEventoTipo)) {
                throw new ArgumentException($"El manejador {manejadorEventoTipo} fue registrado anteriormente por {eventoNombre}");
            }

            _manejadores[eventoNombre].Add(manejadorEventoTipo);

            var factory = new ConnectionFactory() {
                HostName = "localhost",
                DispatchConsumersAsync = true
            };

            var connection = factory.CreateConnection();
            var channel = connection.CreateModel();

            channel.QueueDeclare(eventoNombre, false, false, false, null);

            var consumer = new AsyncEventingBasicConsumer(channel);

            consumer.Received += Consumer_Delegate;

            channel.BasicConsume(eventoNombre, true, consumer);
        }

        private async Task Consumer_Delegate(object sender, BasicDeliverEventArgs e)
        {
            var nombreEvento = e.RoutingKey;
            var message = Encoding.UTF8.GetString(e.Body.ToArray());

            try
            {
                if (_manejadores.ContainsKey(nombreEvento)) {
                    var subscriptions = _manejadores[nombreEvento];

                    foreach (var sb in subscriptions) {
                        var manejador = Activator.CreateInstance(sb);

                        if (manejador == null) continue;

                        var tipoEvento = _eventoTipos.SingleOrDefault(x => x.Name == nombreEvento);

                        var eventoDS = JsonConvert.DeserializeObject(message, tipoEvento);

                        var concretoTipo = typeof(IEventoManejador<>).MakeGenericType(tipoEvento);

                        await (Task)concretoTipo.GetMethod("Handle").Invoke(manejador, new object[] { eventoDS});
                    }
                }
            }
            catch (Exception ex) { 

            }
        }
    }
}
