using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nick.Plugin.Jellyscrub.Api;

public class DeleteOptions
{
    public bool ForceDelete { get; set; }
    public bool DeleteNonEmpty {  get; set; }
}
