# MediaBrowser.Plugins.VuPlus
MediaBrowser Vu+ Plugin

Plugin foor MediaBrowser to allow it to view channels, browser epg, set/delete timers, play recordings etc.

Known Issues / Limitations
--------------------------
 
Genres are set to 'Unknown'.
No Series related functionality.
If paths are entered on the config page then remember to end them with a slash
 
 
Setup on Vu+ / Enigma2

----------------------
Ensure openWebIf is installed and running - go into settings and make a note of them so they can be entered on the MediaBrowser Vu+ config page.
 
 
Setup on MediaBrowser server
----------------------------

Install Vu+ plugin and restart MediaBrowser server.
Go to config page for Vu+ and amend default contents as follows (at a minimum, the first 3 must be entered):
 
Vu+ hostname or ip address:
The host name (address) or ip address of your Vu+ receiver
 
Vu+ streaming port:
The Streaming port of your Vu+ receiver eg. 8001 / 8002
 
Vu+ Web Interface port:
The web Interface port of your receiver eg. 8000
 
Vu+ Web Interface username:
The web Interface username of your receiver (optional)
Vu+ Web Interface password:
The web Interface password of your receiver (optional)
 
Use secure HTTP (HTTPS):
Use HTTPS instead of HTTPS to connect to your receiver
                       
Fetch Only one TV bouquet:
Limit channels to only those contained within the specified TV Bouquet below (optional)
Vu+ TVBouquet:
The TV Bouquet to load channels for (optional - only required if 'Fetch Only one TV bouquet' set)
                            
Zap before Channel switch (single tuner)
Set if only one tuner within receiver to make tuner jump to channel
 
Fetch picons from webinterface:
Set if you want to retrieve Picons from the web interface of the receiver 
Picons Path:
The local location of your Picons (must end with appropriate slash)  eg. C:\Picons\ (optional - only required if 'Fetch picons from webinterface' is not set)
 
Recording Path:
The location to store your recordings on your receiver  (must end with appropriate slash) eg. /hdd/movie/ (optional)
 
Enable VuPlus debug logging:
Plugin Debugging
 
