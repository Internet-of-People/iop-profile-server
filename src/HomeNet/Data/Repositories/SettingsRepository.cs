using HomeNet.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HomeNet.Data.Repositories
{
  /// <summary>
  /// Database key-value repository for node settings.
  /// </summary>
  public class SettingsRepository : GenericRepository<Setting>
  {
    /// <summary>
    /// Creates instance of the setting repository.
    /// </summary>
    /// <param name="context">Database context.</param>
    public SettingsRepository(Context context)
      : base(context)
    {
    }

    /// <summary>
    /// Obtains integer value from node settings.
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
    /// Obtains string value from node settings.
    /// </summary>
    /// <param name="Name">Setting name.</param>
    /// <returns>String value of the setting or null if the setting with corresponding name does not exist.</returns>
    public string GetStringSetting(string Name)
    {
      Setting setting = context.Settings.SingleOrDefault(s => s.Name == Name);
      return setting != null ? setting.Value : null;
    }
  }
}
