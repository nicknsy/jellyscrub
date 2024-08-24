using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;

namespace Nick.Plugin.Jellyscrub.Conversion;

public class ConvertInfo
{
    public BaseItem Item { get; set; }
    public string Path { get; set; }
    public int Width { get; set; }
}
