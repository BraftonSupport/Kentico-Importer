===========================================
Castleford content importer for Kentico CMS
===========================================

The Castleford content importer allows Kentico users to automatically import new or updated content straight from the Castleford 
content API.

1. Supported versions
---------------------

Kentico 8.0, 8.1, 8.2 and 9.0.

2. Preparation
--------------

Warning: installing this package will cause a cache rebuild and momentary downtime. If you're subject to an uptime SLA then please 
perform this installation during a scheduled maintenence period.

Required settings: 

- Set 'Combine with default culture' (under the 'Content' settings group) to true. 

3. Installation
---------------

3.1. Extract archive

Extract the provided archive https://github.com/BraftonSupport/Kentico-Importer/archive/master.zip. You will see the following:

App_Code
ImportPackage
ImportPackage.zip
readme.txt

3.1. Import install package

 - Open the 'Sites' application
 
 - Click 'Import site or objects'
 
 - Select 'Upload package' and use the file browser to select 'ImportPackage.zip' and click 'Next'
 
 - In the objects selection screen, make sure that all items in the following are checked for import:
 
   - Custom tables
   - Modules
   - Scheduled tasks
   - Settings keys
   
   No further selections are necessary. Click 'Next'
   
 - Wait for the import to complete 

3.2. Assign module to site

 - Open the 'Modules' application
 
 - Click the green pencil 'Edit' icon next to the module 'Castleford Importer'
 
 - Go to the 'Sites' submenu and assign the module to each site that you wish to import content into.

3.3. Copy custom classes

 - Copy the contents of the provided App_Code folder to the App_Code folder of your target install.
 
 - Wait for the cache to rebuild. 

4. Configuration and first use
------------------------------

 - Open the 'Settings' application
 
 - In the 'Castleford' settings group, enter the following information:
   - Content feed URL
   - Import target locations
   - Page template
   
 - In the 'Castleford.Mappings' settings group, enter the fields that should hold content published by the API. The default 
   settings will work for the CMS.BlogPost class.
 
   Your best reference for choosing these fields are:
    - K9 Api Reference (http://devnet.kentico.com/documentation/kentico-9)
    - k8 Api Reference (http://devnet.kentico.com/documentation/older-documentation)
    - And the field definitions found in the 'Document Types' application
	
   Some types have manditory fields (usually title + created date fields). Failure to provide mappings to these fields will 
   cause the import process to fail.

 ----

 - Open the 'Scheduled Tasks' application
 
 - Click the green pencil 'Edit' icon next to the task 'CastlefordImport'
 
 - If you would like the importer to check for new and updated articles on a regular basis:
   - Set 'Task Enabled'
   - Enter your desired time interval (default is every hour)
   - Click 'Save'
 
 - You may wish to run the importer task manually to confirm that the importer is operating correctly. Back in the 'Scheduled Tasks'
   application, click the green 'Play' button next to the CastleFordImport task to run it manually.
   
   If the process is successful, the task will the number of articles imported/updated. If unsuccessful, an error message will be
   provided. In the event of an error, full Exception details are provided in the Kentico Event Log.

5. Uninstall
------------

To remove this package, perform the following steps:

 - Delete the 'castleford.imports' custom table
 - Delete 'ImporterTask.cs', 'KenticoHelper.cs' and 'KenticoLogger.cs' from /{your_site}/CMS/App_Code
   - This action will cause a cache rebuild