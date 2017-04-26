using IopServerCore.Data;
using ProfileServer.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProfileServer.Data.Repositories
{
  /// <summary>
  /// Database key-value repository for profile server settings.
  /// </summary>
  public class SettingsRepository : GenericRepository<Setting>
  {
    /// <summary>
    /// Creates instance of the setting repository.
    /// </summary>
    /// <param name="Context">Database context.</param>
    /// <param name="UnitOfWork">Instance of unit of work that owns the repository.</param>
    public SettingsRepository(Context Context, UnitOfWork UnitOfWork)
      : base(Context, UnitOfWork)
    {
    }

    /// <summary>
    /// Obtains integer value from profile server settings.
    /// </summary>
    /// <param name="Name">Setting name.</param>
    /// <returns>Integer value of the setting or -1 if the setting with corresponding name does not exist or if it is not an integer setting.</returns>
    public int GetIntSetting(string Name)
    {
      Setting setting = context.Settings.SingleOrDefault(s => s.Name == Name);

      int result = -1;
      if (setting != null)
      {
        if (!int.TryParse(setting.Value, out result))
          result = -1;
      }

      return result;
    }

    /// <summary>
    /// Obtains string value from profile server settings.
    /// </summary>
    /// <param name="Name">Setting name.</param>
    /// <returns>String value of the setting or null if the setting with corresponding name does not exist.</returns>
    public string GetStringSetting(string Name)
    {
      Setting setting = context.Settings.SingleOrDefault(s => s.Name == Name);
      return setting != null ? setting.Value : null;
    }

    /// <summary>
    /// Clears all entries from settings table.
    /// </summary>
    public void Clear()
    {
      List<Setting> allSettings = context.Settings.ToList();
      context.Settings.RemoveRange(allSettings);
    }


    /// <summary>
    /// Adds a record if it does not exists, or updates a value of existing record.
    /// </summary>
    /// <param name="Record">Record to add/update.</param>
    public async Task AddOrUpdate(Setting Record)
    {
      Setting existingSetting = context.Settings.SingleOrDefault(s => s.Name == Record.Name);
      if (existingSetting != null)
      {
        existingSetting.Value = Record.Value;
        Update(existingSetting);
      }
      else await InsertAsync(Record);
    }
  }
}
