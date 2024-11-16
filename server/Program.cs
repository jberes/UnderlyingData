using Microsoft.AspNetCore.Mvc;
using Reveal.Sdk;
using Reveal.Sdk.Dom;
using Reveal.Sdk.Dom.Visualizations;
using RevealSdk.Server;
using System.Text;
using File = System.IO.File;
using RevealSdk.SqlServer.Models;
using Reveal.Sdk.Dom.Filters;
using System.Globalization;
using System.Text.RegularExpressions;

List<IVisualization> _visualizations = [];
List<VisualizationInfo> _visualizationInfo = [];
List<string> _fields = [];
List<string> _details = [];
List<string> _vizs = [];

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddReveal(builder =>
{
    builder
        .AddDataSourceProvider<DataSourceProvider>()
        .AddSettings(settings =>
        {
            settings.License = GlobalSettings.RevealLicenseKey;
        });
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

app.Map("/isduplicatename/{name}", (string name) =>
{
    var filePath = Path.Combine(Environment.CurrentDirectory, "Dashboards");
    return File.Exists($"{filePath}/{name}.rdash");
});

// only used for custom save on server
app.MapPost("/dashboards/{name}", async (HttpRequest request, string name) =>
{
    var filePath = Path.Combine(Environment.CurrentDirectory, $"Dashboards/{name}.rdash");
    using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
    await request.Body.CopyToAsync(stream);
});

// only used for custom save on server
app.MapPut("/dashboards/{name}", async (HttpRequest request, string name) =>
{
    var filePath = Path.Combine(Environment.CurrentDirectory, $"Dashboards/{name}.rdash");
    if (!File.Exists(filePath))
        return;
    using var stream = File.Open(filePath, FileMode.Open);
    await request.Body.CopyToAsync(stream);
});

Task<FileName> GetFileDataAsync(string filePath)
{
    return Task.FromResult(new FileName
    {
        Name = Path.GetFileNameWithoutExtension(filePath)
    });
}

app.MapGet("/dashboards", async ([FromQuery] FileDataMode mode) =>
{
    string folderPath = Path.Combine(Directory.GetCurrentDirectory(), "Dashboards");
    if (Directory.Exists(folderPath))
    {
        var files = Directory.GetFiles(folderPath);
        Random rand = new();

        if (mode == FileDataMode.NameOnly)
        {
            var fileNames = files.Select(file => new FileName { Name = Path.GetFileNameWithoutExtension(file) }).ToList();
            return Results.Ok(fileNames);
        }
        else
        {
            var fileData = await Task.WhenAll(files.Select(async file =>
            {
                var fileInfo = new FileInfo(file);
                var thumbnailInfo = mode == FileDataMode.WithThumbnail ? await GetThumbnailInfo(file) : null;
                var fakeOwnerInfo = GetRandomOwnerName();
                int randomNumber = rand.Next(1, 10);

                return new FileData
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    DateCreated = fileInfo.CreationTime.ToString("dd-MMM-yyyy"),
                    DateModified = fileInfo.LastWriteTime.ToString("dd-MMM-yyyy"),
                    FakeOwner = fakeOwnerInfo.Name,
                    FakeOwnerImageUrl = fakeOwnerInfo.ImageUrl,
                    FakeDashboardImageUrl = @"https://users.infragistics.com/reveal/images/dashboardthumbs/" + $"{randomNumber.ToString()}" + ".png",
                    ThumbnailInfo = thumbnailInfo
                };
            }));
            return Results.Ok(fileData);
        }
    }
    return Results.NotFound();
}).Produces<IEnumerable<FileData>>(StatusCodes.Status200OK)
.Produces<IEnumerable<FileName>>(StatusCodes.Status200OK) // Produces list of FileName objects for NameOnly mode
.Produces(StatusCodes.Status404NotFound);

async Task<IDictionary<string, object>> GetThumbnailInfo(string filePath)
{
    var dashboard = new Dashboard(filePath);
    var info = await dashboard.GetInfoAsync(Path.GetFileNameWithoutExtension(filePath));
    return info.Info;
}

app.MapGet("/dashboards/{name}/thumbnail", async (string name) =>
{
    var path = "dashboards/" + name + ".rdash";
    if (File.Exists(path))
    {
        var dashboard = new Dashboard(path);
        var info = await dashboard.GetInfoAsync(Path.GetFileNameWithoutExtension(path));
        return TypedResults.Ok(info);
    }
    else
    {
        return Results.NotFound();
    }
});

// SDK DOM Code
app.MapGet("dashboards/{name}/visualizations", (string name) =>
{
    try
    {
        var filePath = "Dashboards/" + name + ".rdash";
        if (!File.Exists(filePath))
        {
            return Results.NotFound($"Dashboard file {name}.rdash not found.");
        }

        var document = RdashDocument.Load(filePath);
        var visualizationInfoList = new List<VisualizationNames>();

        foreach (var viz in document.Visualizations)
        {
            var v = new VisualizationNames
            {
                Title = viz.Title,
                Id = viz.Id
            };

            Type vizType = viz.GetType();
            v.Name = vizType.Name;
            v.ChartType = GetDisplayName(vizType.Name);
            v.ImageUrl = GetImageUrl(vizType.Name);

            visualizationInfoList.Add(v);
        }

        return Results.Ok(visualizationInfoList);
    }
    catch (Exception ex)
    {
        return Results.Problem($"An error occurred: {ex.Message}");
    }

}).Produces<IEnumerable<VisualizationNames>>(StatusCodes.Status200OK)
  .Produces(StatusCodes.Status404NotFound)
  .Produces(StatusCodes.Status500InternalServerError);

app.MapPut("dashboards/{name}/visualizations/{id}/", async (string name, string id, IEnumerable<FieldUpdateRequest> fieldUpdates) =>
{
    var filePath = "dashboards/" + name + ".rdash";
    if (File.Exists(filePath))
    {
        var document = RdashDocument.Load(filePath);
        var viz = document.Visualizations.FindById(id);

        if (viz != null)
        {
            var x = viz.DataDefinition.AsTabular();

            foreach (var fieldUpdate in fieldUpdates)
            {
                var field = x.Fields.FirstOrDefault(f => f.FieldName == fieldUpdate.FieldName);
                if (field != null)
                {
                    field.FieldLabel = fieldUpdate.FieldLabel;
                }
            }
            document.Save(filePath);
            return TypedResults.NoContent();
        }
    }
    return Results.NotFound();
}).Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status404NotFound);


app.MapGet("dashboards/{name}/visualizations/{id}/fields", (string name, string id) =>
{
    var filePath = "dashboards/" + name + ".rdash";
    if (File.Exists(filePath))
    {
        var document = RdashDocument.Load(filePath);
        var viz = document.Visualizations.FindById(id); // old SDK was 'as ITabularVisualization';
        var x = viz.DataDefinition.AsTabular();
        var fieldsData = x.Fields // old SDK was 'viz.DataDefinition.Fields'
            .Select(field => new
            {
                field.FieldName,
                field.FieldLabel,
                field.GetType().Name
            })
            .ToList();

        return TypedResults.Ok(fieldsData);
    }
    return Results.NotFound();
}).Produces<IEnumerable<IField>>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

app.MapGet("dashboards/{name}/visualizations/{id}/details", (string name, string id) =>
{
    _details.Clear();
    var filePath = "dashboards/" + name + ".rdash";
    if (File.Exists(filePath))
    {
        var document = RdashDocument.Load(filePath);
        var viz = document.Visualizations.FindById(id); // old SDK was 'as ITabularVisualization';
        var x = viz.DataDefinition.AsTabular();

        List<VisualizationInfo> visualizationInfoList = [];

        Type vizType = x.GetType();
        VisualizationInfo info = new()
        {
            Id = viz.Id,
            Title = viz.Title,
            FullName = vizType.FullName,
            Name = vizType.Name,
            ChartType = viz.ChartType.ToString(),
            ImageUrl = GetImageUrl(vizType.Name),
            Labels = [],
            Values = [],
            Rows = [],
            Targets = []
        };

        if (viz is ILabels iLabels)
        {
            foreach (var label in iLabels.Labels)
            {
                info.Labels.Add(label.DataField.FieldName);
            }
        }

        if (viz is IValues iValues)
        {
            foreach (var value in iValues.Values)
            {
                info.Values.Add(value.DataField.FieldName);
            }
        }

        if (viz is IRows iRows)
        {
            foreach (var value in iRows.Rows)
            {
                info.Rows.Add(value.DataField.FieldName);
            }
        }

        if (viz is ITargets iTargets)
        {
            foreach (var value in iTargets.Targets)
            {
                info.Targets.Add(value.DataField.FieldName);
            }
        }

        visualizationInfoList.Add(info);
        //}
        return TypedResults.Ok(visualizationInfoList.ToList());
    }

    return Results.NotFound();

}).Produces<IEnumerable<VisualizationInfo>>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

app.MapDelete("dashboards/{name}/delete", (string fileName) =>
{
    try
    {
        fileName += ".rdash";
        string folderPath = "Dashboards"; 
        folderPath = Path.Combine(Directory.GetCurrentDirectory(), folderPath);

        if (Directory.Exists(folderPath))
        {
            string filePath = Path.Combine(folderPath, fileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return Results.Ok($"File '{fileName}' deleted successfully.");
            }
            else
            {
                return Results.NotFound($"File '{fileName}' not found in the 'Dashboards' folder.");
            }
        }
        else
        {
            return Results.NotFound("Folder 'Dashboards' not found.");
        }
    }
    catch (Exception ex)
    {
        return Results.BadRequest($"Error: {ex.Message}");
    }
});

string GetImageUrl(string input)
{
    const string visualizationSuffix = "Visualization";
    if (input.EndsWith(visualizationSuffix, StringComparison.OrdinalIgnoreCase))
    {
        input = input.Substring(0, input.Length - visualizationSuffix.Length).TrimEnd();
    }

    var dashboardImagePath = @"https://users.infragistics.com/reveal/dashboard-images/";
    //var datasourceImagePath = @"https://users.infragistics.com/reveal/datasource-images/";

    return $"{dashboardImagePath}{input}.png";
}

string GetDisplayName(string input)
{
    const string visualizationSuffix = "Visualization";
    if (input.EndsWith(visualizationSuffix, StringComparison.OrdinalIgnoreCase))
    {
        input = input.Substring(0, input.Length - visualizationSuffix.Length).TrimEnd();
    }

    StringBuilder friendlyNameBuilder = new(input.Length);
    foreach (char currentChar in input)
    {
        if (friendlyNameBuilder.Length > 0 && char.IsUpper(currentChar))
        {
            friendlyNameBuilder.Append(' ');
        }

        friendlyNameBuilder.Append(currentChar);
    }
    return friendlyNameBuilder.ToString().Trim();
}

FakeOwnerInfo GetRandomOwnerName()
{
    var random = new Random();
    var owners = new FakeOwnerInfo[]
    {
        new() { Name = "Alex Wilber", ImageUrl = "https://users.infragistics.com/reveal/images/faces/AlexWilber.jpg" },
        new() { Name = "Diego Siciliani", ImageUrl = "https://users.infragistics.com/reveal/images/faces/DiegoSiciliani.jpg" },
        new() { Name = "Grady Archie", ImageUrl = "https://users.infragistics.com/reveal/images/faces/GradyArchie.jpg" },
        new() { Name = "Henrietta Mueller", ImageUrl = "https://users.infragistics.com/reveal/images/faces/HenriettaMueller.jpg" },
        new () { Name = "Isaiah Langer", ImageUrl = "https://users.infragistics.com/reveal/images/faces/IsaiahLanger.jpg" },
        new () { Name = "Johanna Lorenz", ImageUrl = "https://users.infragistics.com/reveal/images/faces/JohannaLorenz.jpg" },
        new () { Name = "Joni Sherman", ImageUrl = "https://users.infragistics.com/reveal/images/faces/JoniSherman.jpg" },
        new () { Name = "Lee Gu", ImageUrl = "https://users.infragistics.com/reveal/images/faces/LeeGu.jpg" },
        new () { Name = "Lidia Holloway", ImageUrl = "https://users.infragistics.com/reveal/images/faces/LidiaHolloway.jpg" },
        new () { Name = "Lynne Robbins", ImageUrl = "https://users.infragistics.com/reveal/images/faces/LynneRobbins.jpg" },
        new () { Name = "Megan Bowen", ImageUrl = "https://users.infragistics.com/reveal/images/faces/MeganBowen.jpg" },
        new () { Name = "Miriam Graham", ImageUrl = "https://users.infragistics.com/reveal/images/faces/MiriamGraham.jpg" },
        new () { Name = "Nestor Wilke", ImageUrl = "https://users.infragistics.com/reveal/images/faces/NestorWilke.jpg" },
        new () { Name = "Patti Fernandez", ImageUrl = "https://users.infragistics.com/reveal/images/faces/PattiFernandez.jpg" },
        new () { Name = "Pradeep Gupta", ImageUrl = "https://users.infragistics.com/reveal/images/faces/PradeepGupta.jpg" },
    };

    return owners[random.Next(owners.Length)];
}

app.MapGet("/dashboards/export/{name}", async (string name, string format, IDashboardExporter dashboardExporter) =>
{
    Stream stream;
    string contentType = "application/pdf";
    if (format == "xlsx")
    {
        stream = await dashboardExporter.ExportToExcel(name);
        contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    }
    else if (format == "pptx")
    {
        stream = await dashboardExporter.ExportToPowerPoint(name);
        contentType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";
    }
    else
    {
        stream = await dashboardExporter.ExportToPdf(name);
    }

    return Results.File(stream, contentType);
});

app.MapGet("/dashboards/export/{name}/{visualization}", async (string name, string visualization, string format, IDashboardExporter dashboardExporter) =>
{
    Stream stream;
    string contentType = "application/pdf";
    if (format == "xlsx")
    {
        var options = new ExcelExportOptions();
        options.Visualizations.AddByTitle(visualization);
        stream = await dashboardExporter.ExportToExcel(name, options: options);
        contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    }
    else if (format == "pptx")
    {
        var options = new PowerPointExportOptions();
        options.Visualizations.AddByTitle(visualization);
        stream = await dashboardExporter.ExportToPowerPoint(name, options: options);
        contentType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";
    }
    else
    {
        var options = new PdfExportOptions();
        options.Visualizations.AddByTitle(visualization);
        stream = await dashboardExporter.ExportToPdf(name, options: options);
    }

    return Results.File(stream, contentType);
});


// New code for underlying data 

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