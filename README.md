# Speed up your Clean Architecture development in Visual Studio!

[![Build](https://github.com/neozhu/CleanArchitectureCodeGenerator/actions/workflows/build.yml/badge.svg)](https://github.com/neozhu/CleanArchitectureCodeGenerator/actions/workflows/build.yml)
![Visual Studio Marketplace Version (including pre-releases)](https://img.shields.io/visual-studio-marketplace/v/neozhu.247365)
![Visual Studio Marketplace Downloads](https://img.shields.io/visual-studio-marketplace/d/neozhu.247365?label=Downloads)

[Clean Architecture](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html) is a software design philosophy that separates the elements of a design into ring levels. The main rule of Clean Architecture is that code dependencies can only move from the outer levels inward. Code on the inner layers can have no knowledge of functions on the outer layers. This extension helps you generate code that adheres to this principle.

## Download the extension

[VS Marketplace](https://marketplace.visualstudio.com/items?itemName=neozhu.247365)

[Open VSIX Gallery](https://www.vsixgallery.com/extension/CleanArchitecture_CodeGenerator_BlazorApp)

![image](https://github.com/neozhu/CleanArchitectureCodeGenerator/assets/1549611/fbcce4ee-f14a-47c5-8dd3-37503f4ec52e)
![image](https://github.com/neozhu/CleanArchitectureCodeGenerator/assets/1549611/72b3800a-58e5-4853-ba7e-f1d7a46286be)


### How to start

<div><video controls src="https://user-images.githubusercontent.com/1549611/197116874-f28414ca-7fc1-463a-b887-0754a5bb3e01.mp4" muted="false"></video></div>
-------------------------------------------------

A Visual Studio extension for easily create application features code  to clean architecture project. Simply hit Shift+F2 to create an empty file in the
selected folder or in the same folder as the selected file.

See the [changelog](CHANGELOG.md) for updates and roadmap.


### Features

This extension helps you rapidly scaffold components for your Clean Architecture project:

#### Core Application Layer Components
Quickly generate essential C# classes for your application layer, including:

*   **Commands and Validators:** For operations that change the state of your application (Add/Edit, Create, Delete, Update, Import).
    *   `{nameofPlural}/Commands/AddEdit/AddEdit{name}Command.cs`
    *   `{nameofPlural}/Commands/AddEdit/AddEdit{name}CommandValidator.cs`
    *   `{nameofPlural}/Commands/Create/Create{name}Command.cs`
    *   `{nameofPlural}/Commands/Create/Create{name}CommandValidator.cs`
    *   `{nameofPlural}/Commands/Delete/Delete{name}Command.cs`
    *   `{nameofPlural}/Commands/Delete/Delete{name}CommandValidator.cs`
    *   `{nameofPlural}/Commands/Update/Update{name}Command.cs`
    *   `{nameofPlural}/Commands/Update/Update{name}CommandValidator.cs`
    *   `{nameofPlural}/Commands/Import/Import{name}Command.cs`
    *   `{nameofPlural}/Commands/Import/Import{name}CommandValidator.cs`
*   **Data Transfer Objects (DTOs):** To define how data is sent and received.
    *   `{nameofPlural}/DTOs/{name}Dto.cs`
*   **Event Handlers:** For domain events (Created, Updated, Deleted).
    *   `{nameofPlural}/EventHandlers/{name}CreatedEventHandler.cs`
    *   `{nameofPlural}/EventHandlers/{name}UpdatedEventHandler.cs`
    *   `{nameofPlural}/EventHandlers/{name}DeletedEventHandler.cs`
*   **Queries:** For retrieving data (Export, GetAll, Pagination).
    *   `{nameofPlural}/Queries/Export/Export{nameofPlural}Query.cs`
    *   `{nameofPlural}/Queries/GetAll/GetAll{nameofPlural}Query.cs`
    *   `{nameofPlural}/Queries/Pagination/{nameofPlural}PaginationQuery.cs`

#### TypeScript Definition Generation
Automatically generate TypeScript definition files (`.d.ts`) for your Data Transfer Objects (DTOs), enabling type-safe interaction with your frontend applications.
*   `{nameofPlural}/DTOs/{name}Dto.d.ts`

### CleanArchitecture for Blazor Server Application project
Please use this in collaboration with this project.

Github :[https://github.com/neozhu/RazorPageCleanArchitecture](https://github.com/neozhu/CleanArchitectureWithBlazorServer)
![Clean Architecture With Blazor Server](https://raw.githubusercontent.com/neozhu/CleanArchitectureWithBlazorServer/main/doc/page.png)

### How to use

A new button is added to the context menu in Solution Explorer.

![Add new file dialog](art/menu1.png)

You can either click that button or use the keybord shortcut **Shift+F2**.

Select Entity Name from Domain Project

![Add new file dialog](art/dialog1.png)

### Create folders and namespace

Create additional folders for your file by using forward-slash to
specify the structure.

For example, by typing **scripts/test.js** in the dialog, the
folder **scripts** is created if it doesn't exist and the file
**test.js** is then placed into it.

### Generate sourcecode
![Source code for application features](art/code.png)

### Generate to-do list
![to-do list](art/task-list.png)

### code templates
You can modify these templates according to your own projects
![tempaltes](art/template.png)

## Contribute
Check out the [contribution guidelines](.github/CONTRIBUTING.md)
if you want to contribute to this project.

For cloning and building this project yourself, make sure
to install the
[Extensibility Tools 2015](https://marketplace.visualstudio.com/items?itemName=MadsKristensen.ExtensibilityTools)
extension for Visual Studio which enables some features
used by this project.

## **License**

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
