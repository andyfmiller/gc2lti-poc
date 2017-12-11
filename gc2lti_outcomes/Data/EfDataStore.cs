using System;
using System.Linq;
using System.Threading.Tasks;
using Google.Apis.Util.Store;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Item = gc2lti_outcomes.Models.Item;

namespace gc2lti_outcomes.Data
{
    public class EfDataStore : IDataStore
    {
        private readonly Gc2LtiDbContext _context;
        public EfDataStore(Gc2LtiDbContext context)
        {
            _context = context;
        }

        public async Task ClearAsync()
        {
            await _context.Database.ExecuteSqlCommandAsync("TRUNCATE TABLE [Items]");
        }

        public async Task DeleteAsync<T>(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Key MUST have a value");
            }

            var generatedKey = GenerateStoredKey(key, typeof(T));
            var item = _context.Items.FirstOrDefault(x => x.Key == generatedKey);
            if (item != null)
            {
                _context.Items.Remove(item);
                await _context.SaveChangesAsync();
            }
        }

        public Task<T> GetAsync<T>(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Key MUST have a value");
            }

            var generatedKey = GenerateStoredKey(key, typeof(T));
            var item = _context.Items.FirstOrDefault(x => x.Key == generatedKey);
            var value = item == null ? default(T) : JsonConvert.DeserializeObject<T>(item.Value);
            return Task.FromResult(value);
        }

        public async Task StoreAsync<T>(string key, T value)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Key MUST have a value");
            }

            var generatedKey = GenerateStoredKey(key, typeof(T));
            var json = JsonConvert.SerializeObject(value);

            var item = await _context.Items.SingleOrDefaultAsync(x => x.Key == generatedKey);

            if (item == null)
            {
                _context.Items.Add(new Item { Key = generatedKey, Value = json });
            }
            else
            {
                item.Value = json;
            }

            await _context.SaveChangesAsync();
        }

        private static string GenerateStoredKey(string key, Type t)
        {
            return string.Format("{0}-{1}", t.FullName, key);
        }
    }
}
