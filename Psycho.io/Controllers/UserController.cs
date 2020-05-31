﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Psycho.DAL.Core.Domain;
using Psycho.DTO.Core;
using Psycho.Logic.Facade.Interfaces;
using Psycho.DAL.Core;
using Psycho.DTO.Persistence;
using Newtonsoft.Json;
using Psycho.io.Models.User;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using System.IO;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Psycho.io.Controllers
{
    [Authorize(Roles = "Psychologist,AuthorizedUser,AnonymousUser")]
    public class UserController : PsychoMvcControllerBase
    {
        private UserManager<User> _userManager;
        private IUnitOfWork _unitOfWork;
        private IWebHostEnvironment _appEnvironment;

        public UserController(IUnitOfWork unitOfWork,
            IPsychoLogic psychoLogic,
            UserManager<User> userManager,
            IWebHostEnvironment appEnvironment) : base(psychoLogic) {
            _userManager = userManager;
            _unitOfWork = unitOfWork;
            _appEnvironment = appEnvironment;
        }

        public async Task<IActionResult> ChatAsync(int? friendId = null)
        {
            List<UserChatDTO> chats = new List<UserChatDTO>();

            var user = GetCurrentUserAsync().Result;

            var listChats = await _unitOfWork.Context.Chats.Where(x => x.ReceiverId == user.Id || x.SenderId == user.Id).ToListAsync();

            var listUniq = new List<int>();
            foreach(var elem in listChats)
            {
                if(elem.SenderId != user.Id && !listUniq.Contains(elem.SenderId))
                {
                    listUniq.Add(elem.SenderId);
                }
                else if (elem.ReceiverId != user.Id && !listUniq.Contains(elem.ReceiverId))
                {
                    listUniq.Add(elem.ReceiverId);
                }
            }

            foreach(var friendsId in listUniq)
            {
                List<Chat> chat = new List<Chat>();

                foreach(var message in listChats)
                {
                    if(message.ReceiverId == friendsId || message.SenderId == friendsId)
                    {
                        chat.Add(message);
                    }
                }

                User userSender = _unitOfWork.Users.Get(friendsId);
                chats.Add(new UserChatDTO
                {
                    chat = chat,
                    User = userSender
                });
            }

            var onChatWithOpen = friendId.HasValue
                ? await _unitOfWork.Context.Psychologists.FirstOrDefaultAsync(p => p.Id == friendId.Value)
                : null;
            var chatViewModel = new ChatViewModel
            {
                OnChatWithOpen = onChatWithOpen,
                Chats = chats
            };

            return View(chatViewModel);
        }

        public async Task<IActionResult> Profile()
        {
            var user = await GetCurrentUserAsync();
            var userId = user.Id;
            UserDTO userDTO = PsychoLogic.UserFacade.GetUserDTO(userId);
            return View(userDTO);
        }

        public async Task<IActionResult> Edit(UserDTO model)
        {
            var user = await GetCurrentUserAsync();
            var userId = user.Id;
            UserDTO userDTO = PsychoLogic.UserFacade.GetUserDTO(userId);
            return View(userDTO);
        }

        public IActionResult DashboardPsychologist()
        {
            var userId = GetCurrentUserId();
            Psychologist psychologist = _unitOfWork.Psychologists.Get(userId.Result);
            var appointments = psychologist.Appointments.ToList();

            var model = new PsychologistWithAppointmentsDTO();
            model.Name = $"{psychologist.FirstName} {psychologist.LastName}";
            model.Psychologist = psychologist;
            
            List<AppointmentForCalendarDTO> list = new List<AppointmentForCalendarDTO>();
            foreach(var elem in appointments)
            {
                string sdtm = elem.StartDataTime?.Month <= 9 ? $"0{elem.StartDataTime?.Month}" : $"{elem.StartDataTime?.Month}";
                string sdtd = elem.StartDataTime?.Day <= 9 ? $"0{elem.StartDataTime?.Day}" : $"{elem.StartDataTime?.Day}";
                string sdth = elem.StartDataTime?.Hour <= 9 ? $"0{elem.StartDataTime?.Hour}" : $"{elem.StartDataTime?.Hour}";
                string sdtmin = elem.StartDataTime?.Minute <= 9 ? $"0{elem.StartDataTime?.Minute}" : $"{elem.StartDataTime?.Minute}";


                string edtm = elem.EndDataTime?.Month <= 9 ? $"0{elem.EndDataTime?.Month}" : $"{elem.EndDataTime?.Month}";
                string edtd = elem.EndDataTime?.Day <= 9 ? $"0{elem.EndDataTime?.Day}" : $"{elem.EndDataTime?.Day}";
                string edth = elem.EndDataTime?.Hour <= 9 ? $"0{elem.EndDataTime?.Hour}" : $"{elem.EndDataTime?.Hour}";
                string edtmin = elem.EndDataTime?.Minute <= 9 ? $"0{elem.EndDataTime?.Minute}" : $"{elem.EndDataTime?.Minute}";

                model.appointments.Add(new AppointmentForCalendarDTO
                {
                    id = elem.Id.ToString(),
                    title = $"{elem.AuthorizedUser.FirstName} {elem.AuthorizedUser.LastName}",
                    description = elem.AdditionalInfo,
                    start = $"{elem.StartDataTime?.Year}-{sdtm}-{sdtd}T{sdth}:{sdtmin}:00",
                    end = $"{elem.EndDataTime?.Year}-{edtm}-{edtd}T{edth}:{edtmin}:00"
                });
            }

            model.json = JsonConvert.SerializeObject(model.appointments);
            //model.appointments = list.ToArray();

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> CreateAppointment()
        {
            var userId = await GetCurrentUserId();
            CreateAppointmentDTO createAppointmentDTO = new CreateAppointmentDTO();
            if (User.IsInRole("Psychologist"))
            {
                createAppointmentDTO.CurrentPsychologist = _unitOfWork.Psychologists.Get(userId);
            }
            else if (User.IsInRole("AuthorizedUser"))
            {
                createAppointmentDTO.CurrentAuthorizedUser = _unitOfWork.AuthorizedUsers.Get(userId);
            }
            createAppointmentDTO.Psychologists = _unitOfWork.Psychologists.GetAll().ToList();
            createAppointmentDTO.AuthorizedUsers = _unitOfWork.AuthorizedUsers.GetAll().ToList();

            return PartialView(createAppointmentDTO);
        }

        [HttpPost]
        public IActionResult CreateAppointment(CreateAppointmentDTO model)
        {
            string _status = String.Empty;
            string _description = String.Empty;

            List<Appointment> appointments = _unitOfWork.Psychologists.Get(model.PsychologistId).Appointments.ToList();

            foreach (var elem in appointments)
            {
                if ((model.StartDateTime >= elem.StartDataTime && model.StartDateTime <= elem.EndDataTime) ||
                    (model.EndDateTime >= elem.StartDataTime && model.EndDateTime <= elem.EndDataTime) ||
                    (elem.StartDataTime >= model.StartDateTime && elem.EndDataTime <= model.EndDateTime))
                {
                    _status = "error";
                    _description = "Doctor is busy at that time.";
                    break;
                }
            }

            if (_status != "error")
            {
                Appointment appointment = new Appointment
                {
                    PsychologistId = model.PsychologistId,
                    AuthorizedUserId = model.AuthorizedUserId,
                    StartDataTime = model.StartDateTime,
                    EndDataTime = model.EndDateTime,
                    AdditionalInfo = model.Info,
                    Finished = false
                };

                _unitOfWork.Appointments.Add(appointment);
                int result = _unitOfWork.Complete();

                _status = "success";
                _description = "Appointment created.";
            }
            //if (User.IsInRole("Doctor"))
            {
                //return RedirectToAction("DashboardDoctor", "User", new { showAll = false });
            }
            //else
            {
                //return RedirectToAction("DashboardPatient", "User", new { showAll = false });
            }

            return Json(new { status = _status, description = _description });
        }

        public IActionResult DashboardAuthorizedUser()
        {
            var userId = GetCurrentUserId();
            AuthorizedUser user = _unitOfWork.AuthorizedUsers.Get(userId.Result);
            var appointments = user.Appointments.ToList();

            var model = new AuthorizedUserWithAppointmentsDTO();
            model.Name = $"{user.FirstName} {user.LastName}";
            model.AuthorizedUser = user;
            
            List<AppointmentForCalendarDTO> list = new List<AppointmentForCalendarDTO>();
            foreach (var elem in appointments)
            {
                string sdtm = elem.StartDataTime?.Month <= 9 ? $"0{elem.StartDataTime?.Month}" : $"{elem.StartDataTime?.Month}";
                string sdtd = elem.StartDataTime?.Day <= 9 ? $"0{elem.StartDataTime?.Day}" : $"{elem.StartDataTime?.Day}";
                string sdth = elem.StartDataTime?.Hour <= 9 ? $"0{elem.StartDataTime?.Hour}" : $"{elem.StartDataTime?.Hour}";
                string sdtmin = elem.StartDataTime?.Minute <= 9 ? $"0{elem.StartDataTime?.Minute}" : $"{elem.StartDataTime?.Minute}";


                string edtm = elem.EndDataTime?.Month <= 9 ? $"0{elem.EndDataTime?.Month}" : $"{elem.EndDataTime?.Month}";
                string edtd = elem.EndDataTime?.Day <= 9 ? $"0{elem.EndDataTime?.Day}" : $"{elem.EndDataTime?.Day}";
                string edth = elem.EndDataTime?.Hour <= 9 ? $"0{elem.EndDataTime?.Hour}" : $"{elem.EndDataTime?.Hour}";
                string edtmin = elem.EndDataTime?.Minute <= 9 ? $"0{elem.EndDataTime?.Minute}" : $"{elem.EndDataTime?.Minute}";

                model.appointments.Add(new AppointmentForCalendarDTO
                {
                    id = elem.Id.ToString(),
                    title = $"{elem.Psychologist.FirstName} {elem.Psychologist.LastName}",
                    description = elem.AdditionalInfo,
                    start = $"{elem.StartDataTime?.Year}-{sdtm}-{sdtd}T{sdth}:{sdtmin}:00",
                    end = $"{elem.EndDataTime?.Year}-{edtm}-{edtd}T{edth}:{edtmin}:00"
                });
            }

            model.json = JsonConvert.SerializeObject(model.appointments);
            //model.appointments = list.ToArray();

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> ShowAppointmentResultForDoctorInfo(int appointmentId)
        {
            var appointment = await _unitOfWork.Context.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId);
            var viewModel = new FullAppointmentResultViewModel
            {
                Id = appointmentId,
                Patient = appointment.AuthorizedUser,
                Doctor = appointment.Psychologist,
                Date = appointment.StartDataTime
            };

            var analyses = await _unitOfWork.Context.Analyses.Where(a => a.AppointmentId == appointmentId).ToListAsync();
            viewModel.Analyses = new List<AnalysisViewModel>();
            foreach (var analysis in analyses)
            {
                viewModel.Analyses.Add(new AnalysisViewModel
                {
                    Id = analysis.Id,
                    Name = analysis.Name,
                    AnalysisResult = analysis.AnalysisResult,
                    DoctorConclusion = analysis.DoctorConclusion
                });
            }

            var now = DateTime.Now;
            viewModel.Status = now > appointment?.EndDataTime
                ? "Finished"
                : now < appointment?.StartDataTime
                    ? "Planned"
                    : "Сontinues";

            if(viewModel.Status == "Finished" && !appointment.Finished)
            {
                viewModel.Status = "Pending";
            }

            return PartialView(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> ShowAppointmentResultForPatientInfo(int appointmentId)
        {
            var appointment = await _unitOfWork.Context.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId);
            var viewModel = new FullAppointmentResultViewModel
            {
                Id = appointmentId,
                Patient = appointment.AuthorizedUser,
                Doctor = appointment.Psychologist,
                Date = appointment.StartDataTime
            };

            var analyses = await _unitOfWork.Context.Analyses.Where(a => a.AppointmentId == appointmentId).ToListAsync();
            viewModel.Analyses = new List<AnalysisViewModel>();
            foreach(var analysis in analyses)
            {
                viewModel.Analyses.Add(new AnalysisViewModel
                {
                    Id = analysis.Id,
                    Name = analysis.Name,
                    AnalysisResult = analysis.AnalysisResult,
                    DoctorConclusion = analysis.DoctorConclusion
                });
            }

            var now = DateTime.Now;
            viewModel.Status = now > appointment?.EndDataTime
                ? "Finished"
                : now < appointment?.StartDataTime
                    ? "Planned"
                    : "Сontinues";

            if (viewModel.Status == "Finished" && !appointment.Finished)
            {
                viewModel.Status = "Pending";
            }

            return PartialView(viewModel);
        }

        [HttpGet]
        public IActionResult AddAnalysisPopup(int appointmentId)
        {
            var viewModel = new AnalysisViewModel { AppointmentId = appointmentId };
            return PartialView(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> AddAnalysis(AnalysisViewModel model)
        {
            var analysis = new Analysis
            {
                Name = model.Name,
                AppointmentId = model.AppointmentId,
                AnalysisResult = model.AnalysisResult,
                DoctorConclusion = model.DoctorConclusion
            };

            byte[] fileData = null;
            using (var binaryReader = new BinaryReader(model.File.OpenReadStream()))
            {
                fileData = binaryReader.ReadBytes((int)model.File.Length);
            }

            analysis.File = fileData;
            _unitOfWork.Context.Analyses.Add(analysis);
            await _unitOfWork.Context.SaveChangesAsync();

            model.File = null;
            model.Id = analysis.Id;

            return Ok(model);
        }

        [HttpPost]
        public async Task<IActionResult> RemoveAnalysis(int analysisId)
        {
            _unitOfWork.Context.Analyses.Remove(new Analysis { Id = analysisId });
            await _unitOfWork.Context.SaveChangesAsync();
            return Ok(analysisId);
        }

        [HttpGet]
        public async Task<IActionResult> DownloadAnalysis(int analysisId)
        {
            var analysis = await _unitOfWork.Context.Analyses.FirstOrDefaultAsync(a => a.Id == analysisId);
            return File(analysis.File, "application/pdf", $"analysis-{analysis.Id}.pdf");
        }

        [HttpGet]
        public async Task<IActionResult> FinishAppointmentPopup(int appointmentId)
        {
            var services = await _unitOfWork.Context.Services.AsNoTracking().ToListAsync();
            var viewModel = new FinishAppointmentViewModel
            {
                AppointmentId = appointmentId,
                Services = services
            };

            return PartialView(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> FinishAppointment(int appointmentId, List<int> selectedServiceIds)
        {
            var appointment = await _unitOfWork.Context.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId);
            if(appointment != null)
            {
                appointment.Finished = true;

                var result = new AppointmentResult
                {
                    AppointmentId = appointment.Id,
                    ServicesJson = JsonConvert.SerializeObject(selectedServiceIds)
                };

                _unitOfWork.Context.AppointmentResults.Add(result);
                await _unitOfWork.Context.SaveChangesAsync();

                var processingRecord = new OrderProcessingRecord
                {
                    AppointmentResultId = result.Id,
                    Status = "New"
                };

                _unitOfWork.Context.OrderProcessingRecords.Add(processingRecord);
                await _unitOfWork.Context.SaveChangesAsync();
            }
            
            return Ok();
        }

        [HttpGet]
        public async Task<int> GetCurrentUserId()
        {
            User usr = await GetCurrentUserAsync();
            return usr.Id;
        }

        private Task<User> GetCurrentUserAsync() => _userManager.GetUserAsync(HttpContext.User);
    }
}
