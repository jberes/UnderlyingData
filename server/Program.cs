using Reveal.Sdk;
using Reveal.Sdk.Dom;
using Reveal.Sdk.Dom.Visualizations;
using RevealSdk.Server;
using Reveal.Sdk.Dom.Filters;
using System.Globalization;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddReveal(builder =>
{
    builder
        .AddDataSourceProvider<DataSourceProvider>();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
      builder => builder.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()
    );
});

var app = builder.Build();

app.UseCors("AllowAll");
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/dashboards/names", () =>
{
    try
    {
        string folderPath = Path.Combine(Directory.GetCurrentDirectory(), "Dashboards");
        var files = Directory.GetFiles(folderPath);
        Random rand = new();

        var fileNames = files.Select(file =>
        {
            try
            {
                return new DashboardNames
                {
                    DashboardFileName = Path.GetFileNameWithoutExtension(file),
                    DashboardTitle = RdashDocument.Load(file).Title
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Reading FileData {file}: {ex.Message}");
                return null;
            }
        }).Where(fileData => fileData != null).ToList();

        return Results.Ok(fileNames);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error Reading Directory : {ex.Message}");
        return Results.Problem("An unexpected error occurred while processing the request.");
    }

}).Produces<IEnumerable<DashboardNames>>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status500InternalServerError);

app.MapGet("dashboards/{name}/visualizations/{id}/data",
    (string name, string id,
    bool includeAllFields = false,
    string? filterColumn = null,
    string? filterValue = null,
    bool isDateFilter = false,
    string? formattedValue = null) =>
    {
        var filePath = "dashboards/" + name + ".rdash";
        var document = RdashDocument.Load(filePath);
        if (document == null)
        {
            return Results.NotFound("Dashboard not found.");
        }

        var visualization = document.Visualizations.FindById(id);
        if (visualization == null)
        {
            return Results.NotFound("Visualization not found.");
        }

        string fieldName = null;
        var vizDataSourceItem = (visualization as Visualization).GetDataSourceItem();
        var fields = vizDataSourceItem.Fields;

        foreach (var field in fields)
        {
            if (field is Reveal.Sdk.Dom.Visualizations.DateTimeField)
            {
                fieldName = field.FieldName;
            }
        }

        var newDocument = new RdashDocument("My Dashboard");

        if (isDateFilter && !string.IsNullOrEmpty(filterValue))
        {
            var classificationValue = formattedValue ?? filterValue;
            var formatClassification = ClassifyDateFormat(classificationValue);

            if (DateTime.TryParse(filterValue, null, DateTimeStyles.RoundtripKind, out DateTime parsedDate))
            {
                DateTime startOfPeriod = DateTime.MinValue;
                DateTime endOfPeriod = DateTime.MinValue;

                switch (formatClassification.ToLower())
                {
                    case "year":
                        startOfPeriod = new DateTime(parsedDate.Year, 1, 1);
                        endOfPeriod = new DateTime(parsedDate.Year, 12, 31);
                        break;

                    case "quarter":
                        int quarter = (parsedDate.Month - 1) / 3 + 1;
                        startOfPeriod = new DateTime(parsedDate.Year, (quarter - 1) * 3 + 1, 1);
                        endOfPeriod = startOfPeriod.AddMonths(3).AddDays(-1);
                        break;

                    case "month":
                        startOfPeriod = new DateTime(parsedDate.Year, parsedDate.Month, 1);
                        endOfPeriod = startOfPeriod.AddMonths(1).AddDays(-1);
                        break;

                    case "day":
                        startOfPeriod = parsedDate.Date;
                        endOfPeriod = parsedDate.Date;
                        break;

                    case "hour":
                    case "minute":
                        startOfPeriod = parsedDate;
                        endOfPeriod = parsedDate;
                        break;

                    default:
                        return Results.BadRequest("Invalid date type format.");
                }

                var dateFilter = new DashboardDateFilter()
                {
                    Title = filterColumn,
                    RuleType = DateRuleType.CustomRange,
                    CustomDateRange = new DateRange
                    {
                        From = startOfPeriod,
                        To = endOfPeriod
                    }
                };

                newDocument.Filters.Add(dateFilter);
                var filterBinding = new DashboardDateFilterBinding(filterColumn);
                var gridViz = new GridVisualization(
                    visualization.Title + (!string.IsNullOrEmpty(formattedValue) ? " for " + formattedValue : ""),
                    vizDataSourceItem);
                gridViz.FilterBindings.Add(filterBinding);

                newDocument.Visualizations.Add(gridViz);
                newDocument.Save("dashboards/underlyingdata.rdash");

                return Results.Ok(newDocument);
            }
            else
            {
                return Results.BadRequest("Invalid date format.");
            }
        }
        else if (!isDateFilter && !string.IsNullOrEmpty(filterColumn) && !string.IsNullOrEmpty(filterValue))
        {
            var filterItem = new FilterItem
            {
                FieldValues = new Dictionary<string, object>
            {
                { filterColumn, filterValue }
            }
            };

            var dataFilter = new DashboardDataFilter(vizDataSourceItem)
            {
                Title = filterColumn,
                FieldName = filterColumn,
                AllowMultipleSelection = true,
                AllowEmptySelection = true,
                SelectedItems = new List<FilterItem> { filterItem }
            };

            newDocument.Filters.Add(dataFilter);
            var filterBinding = new DashboardDataFilterBinding(dataFilter);
            var gridViz = new GridVisualization(
                visualization.Title + (!string.IsNullOrEmpty(formattedValue) ? " for " + formattedValue : ""),
                vizDataSourceItem);
            gridViz.FilterBindings.Add(filterBinding);

            newDocument.Visualizations.Add(gridViz);
            newDocument.Save("dashboards/underlyingdata.rdash");

            return Results.Ok(newDocument);
        }
        else
        {
            return Results.BadRequest("Insufficient parameters for filtering.");
        }
    });

string ClassifyDateFormat(string formattedValue)
{
    // Regular expressions for various formats
    var yearFormat = new Regex(@"^\d{4}$");
    var quarterFormat = new Regex(@"^\d{4}-Q\d$|^\d{2}-Q\d$|^Q\d$");
    var monthFormats = new Regex(@"^\w{3}-\d{4}$|^\w{3}-\d{2}$|^\d{2}-\d{4}$|^\d{1}-\d{2}$|^\w{3}$|^\d{2}$|^\d{1}$");
    var dayFormats = new Regex(@"^\d{2}-\w{3}-\d{4}$|^\d{4}-\d{2}-\d{2}$|^\d{2}/\d{2}/\d{4}$|^\d{2}/\d{2}/\d{2}$|^\d{2}-\w{3}$|^\w{3}-\d{2}$|^\d{2}-\d{2}$");
    var hourFormats = new Regex(@"^\d{2}-\w{3}-\d{4} \d{2}:\d{2}$|^\d{2}-\w{3}-\d{2} \d{2}:\d{2}$|^\d{2}-\w{3} \d{2}:\d{2}$|^\d{2}/\d{2}/\d{4} \d{2}:\d{2}$");
    var minuteFormats = new Regex(@"^\d{2}-\w{3}-\d{4} \d{2}:\d{2}$|^\d{2}-\w{3}-\d{2} \d{2}:\d{2}$|^\d{2}-\w{3} \d{2}:\d{2}$");

    if (yearFormat.IsMatch(formattedValue))
        return "Year";
    if (quarterFormat.IsMatch(formattedValue))
        return "Quarter";
    if (monthFormats.IsMatch(formattedValue))
        return "Month";
    if (dayFormats.IsMatch(formattedValue))
        return "Day";
    if (hourFormats.IsMatch(formattedValue))
        return "Hour";
    if (minuteFormats.IsMatch(formattedValue))
        return "Minute";

    return "Unknown format";
}

app.Run();

// ***
// This is a helper class to store the dashboard names
// ***
public class DashboardNames
{
    public string? DashboardFileName { get; set; }
    public string? DashboardTitle { get; set; }
}