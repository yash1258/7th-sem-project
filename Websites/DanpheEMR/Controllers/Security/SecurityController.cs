﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using DanpheEMR.Core.Configuration;
using DanpheEMR.Security;
using DanpheEMR.ServerModel;
using DanpheEMR.DalLayer;
using System.Data.Entity;
using System.Data.SqlClient;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using DanpheEMR.Utilities;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Http.Features;
using DanpheEMR.CommonTypes;

namespace DanpheEMR.Controllers
{

    public class SecurityController : CommonController
    {

        private readonly string fileUploadLocation = null;
        public SecurityController(IOptions<MyConfiguration> _config) : base(_config)
        {
            fileUploadLocation = _config.Value.FileStorageRelativeLocation;
        }
        // GET: api/values
        [HttpGet]
        public string Get(int userId, string reqType)
        {
            DanpheHTTPResponse<object> responseData = new DanpheHTTPResponse<object>();
            try
            {
                if (reqType == "loggedInUser")
                {
                    RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
                    MasterDbContext masterDbContext = new MasterDbContext(connString);
                    string userImgName = (from x in masterDbContext.Employees
                                          where x.EmployeeId == currentUser.EmployeeId
                                          select x.ImageName).FirstOrDefault();

                    EmployeeModel employee = (from x in masterDbContext.Employees
                                              where x.EmployeeId == currentUser.EmployeeId
                                              select x).FirstOrDefault();

                    string imgLocation = string.IsNullOrEmpty(userImgName) ? "" : fileUploadLocation + "UserProfile\\" + userImgName;

                    //start: to get default route for current user.
                    List<RbacRole> usrAllRoles = RBAC.GetUserAllRoles(currentUser.UserId);
                    RbacRole defRole = usrAllRoles != null && usrAllRoles.Count > 0 ? usrAllRoles.OrderBy(r => r.RolePriority).FirstOrDefault() : null;
                    int? defRouteId = defRole != null ? defRole.DefaultRouteId : 0;

                    string defaultRoutePath = null;

                    if (defRouteId.HasValue)
                    {
                        List<DanpheRoute> allRoutes = RBAC.GetAllRoutes();
                        DanpheRoute defRoute = allRoutes.Where(r => r.RouteId == defRouteId.Value).FirstOrDefault();
                        if (defRoute != null)
                        {
                            defaultRoutePath = defRoute.UrlFullPath;
                        }
                    }

                    //end: to get default route for current user.

                    //Ajay 07 Aug 2019
                    //getting LandingPageRouteId
                    var landingPageRouteId = (new RbacDbContext(connString)).Users
                        .Where(a => a.UserId == currentUser.UserId)
                        .Select(a => a.LandingPageRouteId).FirstOrDefault();

                    responseData.Results = new
                    {
                        UserId = currentUser.UserId,
                        UserName = currentUser.UserName,
                        EmployeeId = currentUser.EmployeeId,
                        Profile = new { ImageLocation = imgLocation },
                        NeedsPasswordUpdate = currentUser.NeedsPasswordUpdate,
                        DefaultPagePath = defaultRoutePath,
                        Employee = employee,
                        LandingPageRouteId = landingPageRouteId,
                        IsSysAdmin = defRole.IsSysAdmin
                    };
                    responseData.Status = "OK";
                }
                else if (reqType != null && reqType.ToLower() == "routelist")
                {
                    RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
                    if (currentUser != null)
                    {
                        var currentUserId = currentUser.UserId;
                        List<DanpheRoute> routeList = new List<DanpheRoute>();
                        //we need to get routes with defaultshow=false and no need of hierarchy.
                        routeList = RBAC.GetRoutesForUser(currentUser.UserId, getHiearrchy: false);
                        responseData.Results = routeList;
                        responseData.Status = "OK";
                        //set session of Valid routeList for loggedin user
                        HttpContext.Session.Set<List<DanpheRoute>>("validRouteList", routeList);
                    }
                    else
                    {
                        responseData.Status = "Failed";
                        responseData.ErrorMessage = "User is Not valid";
                    }

                }
                else if (reqType != null && reqType == "validallrouteList")
                {
                    RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
                    if (currentUser != null)
                    {
                        var currentUserId = currentUser.UserId;
                        List<DanpheRoute> routeList = new List<DanpheRoute>();
                        routeList = RBAC.GetRoutesForUser(currentUser.UserId, getHiearrchy: true);

                        var filteredRoutes = routeList.Where(r => r.DefaultShow != false && r.IsActive == true).ToList();
                        filteredRoutes.ForEach(r =>
                        {
                            if (r.ChildRoutes != null)
                            {
                                r.ChildRoutesDefaultShowCount = r.ChildRoutes.Where(c => c.DefaultShow == true).Count();
                            }
                            else
                            {
                                r.ChildRoutesDefaultShowCount = 0;
                            }
                        });
                        responseData.Results = filteredRoutes;
                        responseData.Status = "OK";
                        HttpContext.Session.Set<List<DanpheRoute>>("validallrouteList", filteredRoutes);
                    }
                    else
                    {
                        responseData.Status = "Failed";
                        responseData.ErrorMessage = "User is Not valid";
                    }
                }
                else if (reqType != null && reqType == "userPermissionList")
                {
                    RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
                    List<RbacPermission> userPermissions = new List<RbacPermission>();
                    if (currentUser != null)
                    {
                        int currentUserId = currentUser.UserId;
                        //get permissions of user
                        userPermissions = RBAC.GetUserAllPermissions(currentUserId);
                        //set session of valid user permission
                        HttpContext.Session.Set<List<RbacPermission>>("userAllPermissions", userPermissions);
                        responseData.Status = "OK";
                    }
                    else
                    {
                        responseData.Status = "Failed";
                        responseData.ErrorMessage = "Invalid User.";
                    }

                    responseData.Results = userPermissions;
                }
                else if (reqType == "activeBillingCounter")
                {
                    string activeCounterId = HttpContext.Session.Get<string>("activeBillingCounter");
                    int actCounterId;
                    int.TryParse(activeCounterId, out actCounterId);
                    responseData.Results = actCounterId;
                    responseData.Status = "OK";
                }
                else if(reqType == "activeLab")
                {
                    string activeLabId = HttpContext.Session.Get<string>("activeLabId");
                    int actLabId;
                    int.TryParse(activeLabId, out actLabId);
                    string activeLabName = HttpContext.Session.Get<string>("activeLabName");
                    LabSelectionVM activelab = new LabSelectionVM();
                    activelab.LabTypeId = actLabId;
                    activelab.LabTypeName = activeLabName;
                    responseData.Results = activelab;
                    responseData.Status = "OK";
                }
                else if (reqType == "activePharmacyCounter")
                {
                    string activeCounterId = HttpContext.Session.Get<string>("activePharmacyCounter");
                    int actCounterId;
                    int.TryParse(activeCounterId, out actCounterId);
                    string activeCounterName = HttpContext.Session.Get<string>("activePharmacyCounterName");
                    PHRMCounter counter = new PHRMCounter();
                    counter.CounterId = actCounterId;
                    counter.CounterName = activeCounterName;
                    responseData.Results = counter;
                    responseData.Status = "OK";
                }
                else if (reqType == "get-activeAccHospitalInfo")
                {
                    //this gives currently selected accounting hospital.
                    AccHospitalInfoVM activeHospital = HttpContext.Session.Get<AccHospitalInfoVM>("AccSelectedHospitalInfo");
                    responseData.Results = activeHospital;
                    responseData.Status = "OK";
                }
                else if (reqType == "get-inv-hospitalInfo")
                {
                    var invHospInfo = HttpContext.Session.Get<AccHospitalInfoVM>("INVHospitalInfo");
                    if (invHospInfo != null)
                    {
                        responseData.Status = "OK";
                        responseData.Results = invHospInfo;
                    }
                    else 
                    {
                        InventoryDbContext inventoryDbContext = new InventoryDbContext(connString);
                        AccHospitalInfoVM hospInfo = new AccHospitalInfoVM(); //we are using same model for inventory also
                                                                              //set only TodaysDate,FiscalYearList, CurrentFiscalYear information
                        hospInfo.TodaysDate = DateTime.Now;
                        hospInfo.FiscalYearList = (from fsYear in inventoryDbContext.InventoryFiscalYears
                                                   where fsYear.IsActive == true
                                                   select new FiscalYearModel
                                                   {
                                                       FiscalYearId = fsYear.FiscalYearId,
                                                       FiscalYearName = fsYear.FiscalYearName,
                                                       NpFiscalYearName = fsYear.NpFiscalYearName,
                                                       StartDate = fsYear.StartDate,
                                                       EndDate = fsYear.EndDate,
                                                       CreatedOn = fsYear.CreatedOn,
                                                       CreatedBy = fsYear.CreatedBy.Value,
                                                       IsActive = fsYear.IsActive,
                                                       IsClosed = fsYear.IsClosed,
                                                       ClosedBy = fsYear.ClosedBy,
                                                       ClosedOn = fsYear.ClosedOn
                                                   }).ToList();

                        hospInfo.CurrFiscalYear = (from cf in hospInfo.FiscalYearList.AsEnumerable()
                                                   where (cf.StartDate.Date <= hospInfo.TodaysDate.Date) &&
                                                         (cf.EndDate.Date >= hospInfo.TodaysDate.Date)
                                                   select cf).FirstOrDefault();
                        HttpContext.Session.Set<AccHospitalInfoVM>("INVHospitalInfo", hospInfo);

                        responseData.Status = "OK";
                        responseData.Results = hospInfo;
                    }                   
                }               
            }
            catch (Exception ex)
            {
                responseData.Status = "Failed";
                responseData.ErrorMessage = ex.Message + " exception details:" + ex.ToString();
            }
            var routelist = DanpheJSONConvert.SerializeObject(responseData, true);
            return DanpheJSONConvert.SerializeObject(responseData, true);
        }


        // POST api/values
        [HttpPost]
        public void Post([FromBody]string value)
        {
        }

        //counterid and countername are used for Billing/Pharmacy. 
        //hospitalid used for accounting
        [HttpPut]
        public string Put(string reqType, int counterId, string counterName, int hospitalId, int labId, string labName)
        {
            DanpheHTTPResponse<object> responseData = new DanpheHTTPResponse<object>();

            if (reqType == "activateBillingCounter" && counterId != 0)
            {
                HttpContext.Session.Set<string>("activeBillingCounter", counterId.ToString());
                responseData.Status = "OK";
                responseData.Results = counterId;
            }
            else if (reqType == "activatePharmacyCounter" && counterId != 0)
            {
                HttpContext.Session.Set<string>("activePharmacyCounter", counterId.ToString());
                HttpContext.Session.Set<string>("activePharmacyCounterName", counterName.ToString());
                PHRMCounter counter = new PHRMCounter();
                counter.CounterId = counterId;
                counter.CounterName = counterName;
                responseData.Results = counter;
                responseData.Status = "OK";
                responseData.Results = counter;
            }
            else if(reqType == "activateLab" && labId != 0)
            {
                HttpContext.Session.Set<string>("activeLabId", labId.ToString());
                HttpContext.Session.Set<string>("activeLabName", labName.ToString());
                LabSelectionVM activelab = new LabSelectionVM();
                activelab.LabTypeId = labId;
                activelab.LabTypeName = labName;
                responseData.Status = "OK";
                responseData.Results = activelab;
            }
            else if (reqType == "deActivateBillingCounter")
            {
                HttpContext.Session.Remove("activeBillingCounter");
                responseData.Status = "OK";
            }

            else if (reqType == "deActivatePharmacyCounter")
            {
                HttpContext.Session.Remove("activePharmacyCounter");
                responseData.Status = "OK";
            }
            else if(reqType == "deactivateLab")
            {
                HttpContext.Session.Remove("activeLabId");
                HttpContext.Session.Remove("activeLabName");
                responseData.Status = "OK";
            }
            else if (reqType == "activateAccountingHospital" && hospitalId != 0)
            {

                AccountingDbContext accountingDbContext = new AccountingDbContext(connString);
                AccHospitalInfoVM hospInfo = new AccHospitalInfoVM();
                hospInfo.ActiveHospitalId = hospitalId;
                hospInfo.TodaysDate = DateTime.Now;
                hospInfo.FiscalYearList = (from fsYear in accountingDbContext.FiscalYears
                                           where fsYear.HospitalId == hospitalId
                                           && fsYear.IsActive == true
                                           select fsYear).ToList();

                if (hospInfo.FiscalYearList != null)
                {
                    hospInfo.FiscalYearList.ForEach(f =>
                    {
                        f.CurrentDate = DateTime.Now;
                        f.showreopen = (f.IsClosed == true) ? true : false;
                    });
                }
              
                hospInfo.CurrFiscalYear = (from cf in hospInfo.FiscalYearList.AsEnumerable()                                        
                                           where (cf.StartDate.Date <= hospInfo.TodaysDate.Date) &&
                                                 (cf.EndDate.Date >= hospInfo.TodaysDate.Date)
                                           select cf).FirstOrDefault();

                hospInfo.SectionList = (from s in accountingDbContext.Section
                                        where s.HospitalId == hospitalId
                                        && s.IsActive == true
                                        select s).ToList();
                //assign hospitalnames (long/short) so that it can be displayed in accounting main page on relaod.
                var currHospital = accountingDbContext.Hospitals.Where(h =>h.HospitalId==hospitalId).FirstOrDefault();
                if (currHospital != null)
                {
                    hospInfo.HospitalLongName = currHospital.HospitalLongName;
                    hospInfo.HospitalShortName = currHospital.HospitalShortName;
                }

                //need to set the values int TWO sessions, one for hospitalId only and another for hospital-info-all
                HttpContext.Session.Set<AccHospitalInfoVM>("AccSelectedHospitalInfo", hospInfo);
                HttpContext.Session.Set<int>("AccSelectedHospitalId", hospitalId);

                responseData.Status = "OK";
                responseData.Results = hospInfo;
            }
            else
            {
                responseData.Status = "Failed";
                responseData.ErrorMessage = "Invalid request or invalid CounterId";
            }

            return DanpheJSONConvert.SerializeObject(responseData, true);
        }



        // DELETE api/values/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}

