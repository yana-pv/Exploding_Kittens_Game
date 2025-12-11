using Server.Game.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Models;

public class ClientCardDto
{
    public required CardType Type { get; set; }
    public required string Name { get; set; }
}