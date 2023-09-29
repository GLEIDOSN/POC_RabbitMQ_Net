public class RabbitMQConfig
{
    public ExchangeConfig? Exchange { get; set; }
    public QueueConfig? Queue { get; set; }
}

public class ExchangeConfig
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

public class QueueConfig
{
    public bool Durable { get; set; }
    public bool Exclusive { get; set; }
    public bool AutoDelete { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RoutingKey { get; set; } = string.Empty;
    public Dictionary<string, string>? Arguments { get; set; }
    public bool Active { get; set; }
}