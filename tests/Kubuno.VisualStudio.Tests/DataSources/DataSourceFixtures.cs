using System.Text.Json;
using Kubuno.VisualStudio.Core.DataSources;

namespace Kubuno.VisualStudio.Tests.DataSources
{
    /// <summary>A real `kbdata.read` answer of kubuno-data-tool (SQLite: customers, orders, v_orders) and views to drop onto.</summary>
    internal static class DataSourceFixtures
    {
        public const string KbdataPath = @"C:\app\src\data\shop.kbdata";

        public const string ShopReadJson = @"{""connection"":""Shop"",""name"":""shop"",""provider"":""sqlite"",""queries"":[],""rowNames"":{""customers"":""Customer"",""orders"":""Order"",""v_orders"":""VOrder""},""tables"":[{""columns"":[{""auto_increment"":true,""db_type"":""INTEGER"",""name"":""id"",""nullable"":false,""rust_type"":""i64""},{""db_type"":""TEXT"",""name"":""name"",""nullable"":false,""rust_type"":""String""},{""db_type"":""TEXT"",""name"":""email"",""nullable"":true,""rust_type"":""String""},{""db_type"":""INTEGER"",""name"":""age"",""nullable"":true,""rust_type"":""i64""},{""db_type"":""DATE"",""name"":""birth_date"",""nullable"":true,""rust_type"":""chrono::NaiveDate""},{""db_type"":""BOOLEAN"",""name"":""vip"",""nullable"":false,""rust_type"":""bool""},{""db_type"":""NUMERIC(10,2)"",""max_length"":10,""name"":""balance"",""nullable"":true,""rust_type"":""f64""}],""key"":[""id""],""kind"":""table"",""name"":""customers""},{""columns"":[{""auto_increment"":true,""db_type"":""INTEGER"",""name"":""id"",""nullable"":false,""rust_type"":""i64""},{""db_type"":""INTEGER"",""name"":""customer_id"",""nullable"":false,""rust_type"":""i64""},{""db_type"":""TEXT"",""name"":""item"",""nullable"":false,""rust_type"":""String""},{""db_type"":""NUMERIC(10,2)"",""max_length"":10,""name"":""amount"",""nullable"":false,""rust_type"":""f64""},{""db_type"":""DATETIME"",""name"":""ordered"",""nullable"":true,""rust_type"":""chrono::NaiveDateTime""}],""key"":[""id""],""kind"":""table"",""name"":""orders""},{""columns"":[{""db_type"":""INTEGER"",""name"":""id"",""nullable"":true,""rust_type"":""i64""},{""db_type"":""TEXT"",""name"":""name"",""nullable"":true,""rust_type"":""String""},{""db_type"":""TEXT"",""name"":""item"",""nullable"":true,""rust_type"":""String""},{""db_type"":""NUMERIC(10,2)"",""max_length"":10,""name"":""amount"",""nullable"":true,""rust_type"":""f64""}],""key"":[],""kind"":""view"",""name"":""v_orders""}],""version"":1}";

        /// <summary>The current desktop application template's view (ProjectTemplates/KubunoDesktopApplication/src/main_view.kbview).</summary>
        public const string TemplateView =
            "<!--\n  The main window of app.\n-->\n" +
            "<Panel DesignWidth=\"800\" DesignHeight=\"450\" Title=\"app\" OnLoad=\"main_view_load\">\n" +
            "  <TextField x:Name=\"status\" Placeholder=\"Status\" X=\"24\" Y=\"24\" Width=\"632\" Height=\"36\" Anchor=\"Top, Left, Right\"/>\n" +
            "  <Button x:Name=\"hello\" Text=\"Say hello\" Variant=\"Primary\" X=\"672\" Y=\"24\" Width=\"104\" Height=\"36\" Anchor=\"Top, Right\"/>\n" +
            "</Panel>\n";

        public static KbdataSourceInfo Shop()
        {
            using var document = JsonDocument.Parse(ShopReadJson);
            return KbdataSourceInfo.Parse(KbdataPath, document.RootElement);
        }
    }
}
