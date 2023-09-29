using POC_RabbitMQ.Core.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace POC_RabbitMQ.Worker
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private IConfiguration _configuration;

        public Worker(ILogger<Worker> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var rabbitMq = _configuration.GetSection("RabbitMQ");
            var rabbitMqCfg = _configuration.GetSection("RabbitMQConfigs").Get<List<RabbitMQConfig>>();

            var factory = new ConnectionFactory()
            {
                HostName = rabbitMq["HostName"],
                Port = int.Parse(rabbitMq["Port"]),
                UserName = rabbitMq["UserName"],
                Password = rabbitMq["Password"]
            };

            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();

            if (rabbitMqCfg != null)
            {
                foreach (var config in rabbitMqCfg)
                {
                    IDictionary<string, object>? queueArguments = null;

                    if (config.Queue?.Arguments != null)
                    {
                        queueArguments = config.Queue.Arguments.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
                    };

                    channel.ExchangeDeclare(config.Exchange?.Name, config.Exchange?.Type);
                    channel.QueueDeclare(config.Queue?.Name, config.Queue.Durable, config.Queue.Exclusive, config.Queue.AutoDelete, queueArguments);
                    channel.QueueBind(config.Queue.Name, config.Exchange.Name, config.Queue.RoutingKey);
                }
            }            

            // BasicQos
            //channel.BasicQos(prefetchSize: 0, prefetchCount: 2, global: false);


            var consumer = new EventingBasicConsumer(channel);
            var consumerRetry = new EventingBasicConsumer(channel);

            consumer.Received += (model, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    var mensagemRecebida = JsonSerializer.Deserialize<Message>(message);

                    if (mensagemRecebida != null && mensagemRecebida.TextMessage.ToLower() == "error")
                    {
                        throw new ArgumentException("Simulação de erro.");
                    }

                    Console.WriteLine($"Mensagem recebida: {message}");

                    channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    var mensagemRecebida = JsonSerializer.Deserialize<Message>(message);
                    mensagemRecebida.TextMessage += " retry";
                    var json = JsonSerializer.Serialize(mensagemRecebida);
                    var bytesMessage = Encoding.UTF8.GetBytes(json);
                    channel.BasicPublish("medical_reports.retry.exchange", "", null, bytesMessage);
                    channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, false);
                }
                
            };

            consumerRetry.Received += (model, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    var mensagemRecebida = JsonSerializer.Deserialize<Message>(message);

                    if (ea.BasicProperties.Headers == null)
                    {
                        ea.BasicProperties.Headers = new Dictionary<string, object>();
                        ea.BasicProperties.Headers["retry-count"] = 0;
                        ea.BasicProperties.Headers["x-delay"] = 20000; // 5 segundos
                    }

                    var deliveryCountValue = ea.BasicProperties.Headers.ContainsKey("retry-count") ? (int)ea.BasicProperties.Headers["retry-count"] : 0;


                    if (mensagemRecebida != null && mensagemRecebida.TextMessage.ToLower() == "error retry")
                    {
                        var json = JsonSerializer.Serialize(mensagemRecebida);
                        var bytesMessage = Encoding.UTF8.GetBytes(json);

                        ea.BasicProperties.Headers["retry-count"] = deliveryCountValue + 1;
                        throw new ArgumentException($"Descrição do erro.");
                    }

                    Console.WriteLine($"Mensagem recebida: {message}");

                    channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    if ((int)ea.BasicProperties.Headers["retry-count"] <= 3)
                    {
                        Console.WriteLine("Erro ao processar registro. Tentativa: {0}, Erro: {1}", ea.BasicProperties.Headers["retry-count"], ex.Message);
                        var body = ea.Body.ToArray();
                        var properties = channel.CreateBasicProperties();
                        properties.Persistent = true;
                        properties.Headers = ea.BasicProperties.Headers;
                        channel.BasicPublish("medical_reports.retry.exchange", "", properties, body);
                        channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                    }
                    else
                    {
                        Console.WriteLine("Erro ao processar registro. Número de tentativas excedidas. Erro: {0}", ex.Message);
                        channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, false);
                    }
                }
            };

            channel.BasicConsume(queue: "medical_reports.created.xpto", autoAck: false, consumer: consumer);
            channel.BasicConsume(queue: "medical_reports.retry.xpto", autoAck: false, consumer: consumerRetry);

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
        }
    }
}
