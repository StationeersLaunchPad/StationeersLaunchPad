using System.Collections.Generic;
using System.Xml.Serialization;

namespace StationeersLaunchPad.News;

[XmlRoot("NewsFeed")]
public class NewsFeed
{
  [XmlElement("Entry")]
  public List<NewsEntry> Entries = [];
}

public class NewsEntry
{
  [XmlAttribute("id")]
  public string Id;

  [XmlAttribute("type")]
  public string Type;

  [XmlAttribute("severity")]
  public string Severity;

  [XmlAttribute("heading")]
  public string Heading;

  [XmlElement("short_description")]
  public string ShortDescription;

  [XmlElement("long_description")]
  public string LongDescription;

  [XmlElement("Trigger")]
  public NewsTrigger Trigger;

  [XmlElement("Actions")]
  public NewsActions Actions;
}

public class NewsTrigger
{
  [XmlAttribute("match_type")]
  public string MatchType;

  [XmlAttribute("workshop_id")]
  public string WorkshopId;

  [XmlAttribute("mod_name")]
  public string ModName;

  [XmlAttribute("version_below")]
  public string VersionBelow;
}

public class NewsActions
{
  [XmlElement("Primary")]
  public NewsAction Primary;

  [XmlElement("Secondary")]
  public NewsAction Secondary;
}

public class NewsAction
{
  [XmlAttribute("label")]
  public string Label;

  [XmlAttribute("action")]
  public string Action;

  [XmlAttribute("url")]
  public string Url;

  [XmlAttribute("modid")]
  public string ModId;
}
