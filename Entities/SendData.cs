﻿using System.Text.Json.Serialization;

namespace NewsletterBuilder.Entities;

public class SendData
{
  [JsonPropertyName("to")]
  public string To { get; init; }
}