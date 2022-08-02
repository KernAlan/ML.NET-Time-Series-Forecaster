# ML.NET-Time-Series-Forecaster
An ML.NET time series forecaster using the SSA algorithm engine. This is setup out-of-the-box to intake month periods and predict over a year, but these variables are all configurable.

NOTE: You will need to install ML.NET and ML.NET.TimeSeries NuGet packages.

If this doesn't work out of the box, the only code you really need is the RevenueForecaster.cs class as well as the models. Create a new .NET class or console project and copy/paste the code from these files when you have the two ML NET packages installed.