const alertPages = ['home.html'];

const alertWrapperId = 'jellyscrub-alert-wrapper';
const alertHTML = 
`
<div style="padding: 0 3.3%; margin: 15px 0;">
    <div style="padding: 10px 20px;
                background-color: #590000;
                border: 1px solid #870000;
                opacity: 95%;
                border-radius: 5px;
                letter-spacing: .05rem;
                text-shadow: 1px 0px black;
                display: inline-block;">
                Jellyscrub has been deprecated in favor of native trickplay functionality.
                Please visit the <a href="#/configurationpage?name=Jellyscrub" style="text-decoration: none; color: white; font-weight: bold">plugin config page</a> for converting your .BIF trickplay files.
    <div>
<div>
`


setInterval(() => {
   if (alertPages.some(x => window.location.href.includes(x)) 
        && !document.getElementById(alertWrapperId)) {
       injectAlert();     
   }
}, 250);

function injectAlert() {
    const homeTab = document.getElementById('homeTab');

    if (!homeTab) return;

    const alertWrapper = document.createElement('div');
    alertWrapper.id = alertWrapperId;
    alertWrapper.innerHTML = alertHTML;
    homeTab.insertBefore(alertWrapper, homeTab.firstChild);
}
