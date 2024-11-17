# UI Features

I used these HTML files for the 1.7 / Q4 2024 video.  Use the reveal.sdk.dom repo for the server.

https://github.com/jberes/RevealDomServer

https://youtu.be/_KgWsXXaa60

![image](https://github.com/user-attachments/assets/8952c312-cbe1-4a36-8bfe-0ac6e4522857)

Sure! Based on the content of your file, here's a summary and documentation for your GitHub `README.md`:

---

## Reveal SDK - Dashboard Viewer

### Summary
The `Reveal SDK - Dashboard Viewer` is a tool designed to integrate and display interactive dashboards within your applications. It leverages the capabilities of the Reveal SDK to provide a seamless and efficient way to visualize data.

### Documentation

#### Features
- **Interactive Dashboards**: Embed fully interactive dashboards into your application.
- **Data Integration**: Connect to various data sources to populate your dashboards.
- **Customization**: Customize the look and feel of your dashboards to match your application's design.
- **Performance**: Optimized for performance to handle large datasets efficiently.

#### Installation
To install the `Reveal SDK - Dashboard Viewer`, follow these steps:

1. **Install the SDK**:
   ```bash
   npm install reveal-sdk
   ```

2. **Import the SDK in your project**:
   ```javascript
   import Reveal from 'reveal-sdk';
   ```

3. **Initialize the Dashboard Viewer**:
   ```javascript
   const viewer = new Reveal.DashboardViewer({
       container: '#dashboardContainer',
       dashboard: 'path/to/your/dashboard'
   });
   ```

#### Usage
1. **Create a Dashboard**:
   - Use the Reveal Designer to create and configure your dashboard.
   - Save the dashboard file to your project directory.

2. **Load the Dashboard**:
   ```javascript
   viewer.loadDashboard('path/to/your/dashboard');
   ```

3. **Customize the Viewer**:
   - Adjust settings such as themes, filters, and data sources to tailor the dashboard to your needs.

#### Examples
Here are some examples of how to use the `Reveal SDK - Dashboard Viewer`:

- **Basic Example**:
   ```javascript
   const viewer = new Reveal.DashboardViewer({
       container: '#dashboardContainer',
       dashboard: 'path/to/your/dashboard'
   });
   viewer.loadDashboard();
   ```

- **Advanced Example**:
   ```javascript
   const viewer = new Reveal.DashboardViewer({
       container: '#dashboardContainer',
       dashboard: 'path/to/your/dashboard',
       theme: 'dark',
       filters: {
           dateRange: {
               start: '2024-01-01',
               end: '2024-12-31'
           }
       }
   });
   viewer.loadDashboard();
   ```

#### Support
For support and further documentation, visit the Reveal SDK Documentation.
