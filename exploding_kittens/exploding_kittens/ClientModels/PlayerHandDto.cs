using Newtonsoft.Json;
using System.Collections.Generic;

namespace exploding_kittens.ClientModels
{
    public class PlayerHandDto
    {
        public List<ClientCardDto> Cards { get; set; }

        public PlayerHandDto()
        {
            Cards = new List<ClientCardDto>();
        }

        public static PlayerHandDto FromJson(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<PlayerHandDto>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}